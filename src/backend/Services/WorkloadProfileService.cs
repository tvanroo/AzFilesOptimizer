using Azure;
using Azure.Data.Tables;
using AzFilesOptimizer.Backend.Models;
using Microsoft.Extensions.Logging;

namespace AzFilesOptimizer.Backend.Services;

public class WorkloadProfileService
{
    private readonly TableClient _tableClient;
    private readonly ILogger _logger;

    public WorkloadProfileService(string connectionString, ILogger logger)
    {
        _logger = logger;
        var serviceClient = new TableServiceClient(connectionString);
        _tableClient = serviceClient.GetTableClient("WorkloadProfiles");
        _tableClient.CreateIfNotExists();
    }

    public async Task<List<WorkloadProfile>> GetAllProfilesAsync()
    {
        var profiles = new List<WorkloadProfile>();
        
        await foreach (var entity in _tableClient.QueryAsync<TableEntity>(filter: $"PartitionKey eq 'WorkloadProfile'"))
        {
            profiles.Add(ConvertFromTableEntity(entity));
        }
        
        return profiles.OrderBy(p => p.IsSystemProfile ? 0 : 1).ThenBy(p => p.Name).ToList();
    }

    public async Task<WorkloadProfile?> GetProfileAsync(string profileId)
    {
        try
        {
            var response = await _tableClient.GetEntityAsync<TableEntity>("WorkloadProfile", profileId);
            return ConvertFromTableEntity(response.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<WorkloadProfile> CreateProfileAsync(WorkloadProfile profile)
    {
        profile.RowKey = Guid.NewGuid().ToString();
        profile.CreatedAt = DateTime.UtcNow;
        profile.IsSystemProfile = false; // Only seed data creates system profiles
        
        var entity = ConvertToTableEntity(profile);
        await _tableClient.AddEntityAsync(entity);
        
        _logger.LogInformation("Created workload profile: {ProfileId} - {Name}", profile.ProfileId, profile.Name);
        return profile;
    }

    public async Task<WorkloadProfile> UpdateProfileAsync(WorkloadProfile profile)
    {
        var existing = await GetProfileAsync(profile.ProfileId);
        if (existing == null)
            throw new InvalidOperationException($"Profile {profile.ProfileId} not found");
        
        if (existing.IsSystemProfile)
            throw new InvalidOperationException("Cannot modify system profiles");
        
        profile.UpdatedAt = DateTime.UtcNow;
        profile.CreatedAt = existing.CreatedAt; // Preserve creation date
        
        var entity = ConvertToTableEntity(profile);
        await _tableClient.UpdateEntityAsync(entity, ETag.All, TableUpdateMode.Replace);
        
        _logger.LogInformation("Updated workload profile: {ProfileId} - {Name}", profile.ProfileId, profile.Name);
        return profile;
    }

    public async Task DeleteProfileAsync(string profileId)
    {
        var existing = await GetProfileAsync(profileId);
        if (existing == null)
            return;
        
        if (existing.IsSystemProfile)
            throw new InvalidOperationException("Cannot delete system profiles");
        
        await _tableClient.DeleteEntityAsync("WorkloadProfile", profileId);
        _logger.LogInformation("Deleted workload profile: {ProfileId}", profileId);
    }

    public async Task SeedDefaultProfilesAsync()
    {
        var existingProfiles = await GetAllProfilesAsync();
        if (existingProfiles.Any(p => p.IsSystemProfile))
        {
            _logger.LogInformation("System profiles already exist, skipping seed");
            return;
        }

        _logger.LogInformation("Seeding default workload profiles");
        
        var profiles = GetDefaultProfiles();
        foreach (var profile in profiles)
        {
            var entity = ConvertToTableEntity(profile);
            await _tableClient.UpsertEntityAsync(entity);
            _logger.LogInformation("Seeded profile: {Name}", profile.Name);
        }
    }

    private List<WorkloadProfile> GetDefaultProfiles()
    {
        return new List<WorkloadProfile>
        {
            // 1. CloudShell (Auto-Exclude)
            new WorkloadProfile
            {
                RowKey = "cloudshell-profile",
                Name = "CloudShell (Auto-Exclude)",
                Description = "Azure CloudShell storage accounts are small (typically 5-6 GB), system-managed file shares used for Azure Cloud Shell sessions. These volumes are required to remain in Azure Files and cannot be migrated to ANF. They typically have names containing 'cloudshell', 'cs-', or are in storage accounts with 'cs' prefix. Size is usually 5-6 GB with minimal IOPS requirements.",
                IsSystemProfile = true,
                IsExclusionProfile = true,
                CreatedAt = DateTime.UtcNow,
                PerformanceRequirements = new PerformanceRequirements
                {
                    MinSizeGB = 5,
                    MaxSizeGB = 6,
                    MinIops = 10,
                    MaxIops = 100,
                    LatencySensitivity = "Low",
                    MinThroughputMBps = 1,
                    MaxThroughputMBps = 10
                },
                AnfSuitabilityInfo = new AnfSuitability
                {
                    Compatible = false,
                    Notes = "System requirement - must remain in Azure Files",
                    Caveats = new[] { "Cannot be migrated", "Required for CloudShell functionality" }
                },
                Hints = new DetectionHints
                {
                    NamingPatterns = new[] { "cloudshell", "cs-", "shell-storage" },
                    CommonTags = new[] { "cloudshell", "system" },
                    FileTypeIndicators = Array.Empty<string>(),
                    PathPatterns = Array.Empty<string>()
                }
            },
            
            // 2. FSLogix / VDI Profiles
            new WorkloadProfile
            {
                RowKey = "fslogix-profile",
                Name = "FSLogix / VDI Profiles",
                Description = "FSLogix profile containers and Office containers used for Azure Virtual Desktop (AVD) or Windows 365. These are user profile disks requiring low latency for good user experience. Typically hundreds of small to medium VHD/VHDX files (10-50 GB each). Total volume size varies based on user count. Performance is critical during login storms. Common in Virtual Desktop Infrastructure deployments.",
                IsSystemProfile = true,
                IsExclusionProfile = false,
                CreatedAt = DateTime.UtcNow,
                PerformanceRequirements = new PerformanceRequirements
                {
                    MinSizeGB = 500,
                    MaxSizeGB = 51200, // 50 TB
                    MinIops = 5000,
                    MaxIops = 10000,
                    LatencySensitivity = "High",
                    MinThroughputMBps = 100,
                    MaxThroughputMBps = 2000
                },
                AnfSuitabilityInfo = new AnfSuitability
                {
                    Compatible = true,
                    RecommendedServiceLevel = "Premium",
                    Notes = "ANF often provides better performance and cost for VDI profiles",
                    Caveats = new[] { "Consider Premium or Ultra tier for login storm performance" }
                },
                Hints = new DetectionHints
                {
                    NamingPatterns = new[] { "fslogix", "profiles", "vdi", "avd", "wvd", "office-container", "profile-container" },
                    CommonTags = new[] { "FSLogix", "AVD", "VDI", "Profiles", "WVD", "CitrixProfiles" },
                    FileTypeIndicators = new[] { ".vhd", ".vhdx" },
                    PathPatterns = new[] { "/profiles/", "/users/" }
                }
            },
            
            // 3. SQL Server Database
            new WorkloadProfile
            {
                RowKey = "sql-database-profile",
                Name = "SQL Server Database",
                Description = "Microsoft SQL Server database storage requiring consistent low latency and high IOPS for transactional workloads. Typically contains .mdf (data files) and .ldf (log files). Size ranges from small departmental databases (100 GB) to large enterprise databases (10+ TB). Mission-critical workloads require Premium storage with guaranteed IOPS. Performance directly impacts application response times.",
                IsSystemProfile = true,
                IsExclusionProfile = false,
                CreatedAt = DateTime.UtcNow,
                PerformanceRequirements = new PerformanceRequirements
                {
                    MinSizeGB = 100,
                    MaxSizeGB = 20480, // 20 TB
                    MinIops = 10000,
                    MaxIops = 100000,
                    LatencySensitivity = "VeryHigh",
                    MinThroughputMBps = 200,
                    MaxThroughputMBps = 4000,
                    IoPattern = "Mixed"
                },
                AnfSuitabilityInfo = new AnfSuitability
                {
                    Compatible = true,
                    RecommendedServiceLevel = "Premium",
                    Notes = "Good fit for most scenarios. Verify backup strategy; ANF snapshots can be beneficial.",
                    Caveats = new[] { "Ultra tier recommended for mission-critical workloads" }
                },
                Hints = new DetectionHints
                {
                    NamingPatterns = new[] { "sql", "database", "db", "sqldata", "sqllogs", "mssql" },
                    CommonTags = new[] { "SQL", "SQLServer", "Database", "MSSQL" },
                    FileTypeIndicators = new[] { ".mdf", ".ldf", ".ndf" },
                    PathPatterns = new[] { "/data/", "/logs/", "/sqldata/" }
                }
            },
            
            // 4. SAP / SAP HANA
            new WorkloadProfile
            {
                RowKey = "sap-hana-profile",
                Name = "SAP / SAP HANA",
                Description = "SAP application and SAP HANA in-memory database storage. SAP HANA requires extremely high performance with strict latency requirements (<1ms). SAP applications have moderate requirements. HANA typically uses very large volumes (multi-TB) with high memory-to-storage ratios. Critical for ERP workloads. SAP has specific certification requirements for storage platforms.",
                IsSystemProfile = true,
                IsExclusionProfile = false,
                CreatedAt = DateTime.UtcNow,
                PerformanceRequirements = new PerformanceRequirements
                {
                    MinSizeGB = 500,
                    MaxSizeGB = 102400, // 100 TB
                    MinIops = 50000,
                    MaxIops = 200000,
                    LatencySensitivity = "Ultra",
                    MinThroughputMBps = 1000,
                    MaxThroughputMBps = 10000,
                    IoPattern = "Mixed"
                },
                AnfSuitabilityInfo = new AnfSuitability
                {
                    Compatible = true,
                    RecommendedServiceLevel = "Ultra",
                    Notes = "Verify SAP HANA on ANF certification. Check SAP Notes for specific requirements. HANA data/log/shared have different requirements.",
                    Caveats = new[] { "Must meet SAP certification requirements", "Ultra tier required for HANA data volumes" }
                },
                Hints = new DetectionHints
                {
                    NamingPatterns = new[] { "sap", "hana", "s4hana", "erp", "hanadb", "sapdata", "sapmnt", "saplogs" },
                    CommonTags = new[] { "SAP", "HANA", "S4HANA", "ERP", "SAPApplications" },
                    FileTypeIndicators = Array.Empty<string>(),
                    PathPatterns = new[] { "/hana/data/", "/hana/log/", "/hana/shared/", "/sapmnt/", "/usr/sap/" }
                }
            },
            
            // 5. Oracle Database
            new WorkloadProfile
            {
                RowKey = "oracle-db-profile",
                Name = "Oracle Database",
                Description = "Oracle Database storage for datafiles, redo logs, and archive logs. Requires high performance with consistent low latency for OLTP workloads. Datafiles can be very large (multi-TB). Oracle has specific I/O requirements including direct I/O support. Common in enterprise environments running ERP, financial systems, or custom applications on Oracle.",
                IsSystemProfile = true,
                IsExclusionProfile = false,
                CreatedAt = DateTime.UtcNow,
                PerformanceRequirements = new PerformanceRequirements
                {
                    MinSizeGB = 500,
                    MaxSizeGB = 51200, // 50 TB
                    MinIops = 10000,
                    MaxIops = 100000,
                    LatencySensitivity = "High",
                    MinThroughputMBps = 500,
                    MaxThroughputMBps = 5000,
                    IoPattern = "Mixed"
                },
                AnfSuitabilityInfo = new AnfSuitability
                {
                    Compatible = true,
                    RecommendedServiceLevel = "Premium",
                    Notes = "Verify Oracle on NFS best practices. Check Oracle dNFS compatibility. Different requirements for data files vs. redo logs.",
                    Caveats = new[] { "Requires NFSv3/v4.1 support", "Consider dNFS for optimal performance" }
                },
                Hints = new DetectionHints
                {
                    NamingPatterns = new[] { "oracle", "oracledb", "orcl", "datafiles", "redolog", "archivelog", "ora" },
                    CommonTags = new[] { "Oracle", "OracleDB", "OracleDatabase" },
                    FileTypeIndicators = new[] { ".dbf", ".log", ".ctl", ".arc" },
                    PathPatterns = new[] { "/oradata/", "/u01/", "/archive/", "/redolog/" }
                }
            },
            
            // 6. Kubernetes / Containers
            new WorkloadProfile
            {
                RowKey = "kubernetes-profile",
                Name = "Kubernetes / Containers",
                Description = "Persistent storage for Kubernetes/container workloads via Container Storage Interface (CSI). Can include application data, configuration, logs, or stateful workloads like databases running in containers. Performance requirements vary widely based on containerized application. Modern microservices architecture. May have many small volumes (PVCs).",
                IsSystemProfile = true,
                IsExclusionProfile = false,
                CreatedAt = DateTime.UtcNow,
                PerformanceRequirements = new PerformanceRequirements
                {
                    MinSizeGB = 1,
                    MaxSizeGB = 10240, // 10 TB per PVC
                    MinIops = 100,
                    MaxIops = 50000,
                    LatencySensitivity = "Medium",
                    MinThroughputMBps = 10,
                    MaxThroughputMBps = 2000,
                    IoPattern = "Mixed"
                },
                AnfSuitabilityInfo = new AnfSuitability
                {
                    Compatible = true,
                    RecommendedServiceLevel = "Standard",
                    Notes = "ANF provides excellent integration via Astra Trident/ANF CSI driver. Dynamic provisioning, snapshots, and clones supported.",
                    Caveats = new[] { "Check if Trident/CSI driver already in use", "Service level depends on workload" }
                },
                Hints = new DetectionHints
                {
                    NamingPatterns = new[] { "k8s", "kubernetes", "pvc", "persistent-volume", "container", "aks", "pods" },
                    CommonTags = new[] { "Kubernetes", "K8s", "AKS", "Containers", "PVC", "StatefulSet" },
                    FileTypeIndicators = Array.Empty<string>(),
                    PathPatterns = new[] { "/var/lib/kubelet/" }
                }
            },
            
            // 7. High Performance Computing (HPC)
            new WorkloadProfile
            {
                RowKey = "hpc-profile",
                Name = "High Performance Computing (HPC)",
                Description = "Storage for HPC clusters running computational workloads like scientific simulations, rendering, genomics, financial modeling, or AI/ML training. Requires very high throughput for parallel I/O operations. Often involves large sequential reads/writes across many compute nodes. May have scratch storage (temporary) and long-term storage needs. Performance critical for job completion time.",
                IsSystemProfile = true,
                IsExclusionProfile = false,
                CreatedAt = DateTime.UtcNow,
                PerformanceRequirements = new PerformanceRequirements
                {
                    MinSizeGB = 10240, // 10 TB
                    MaxSizeGB = 102400, // 100 TB
                    MinIops = 50000,
                    MaxIops = 100000,
                    LatencySensitivity = "Medium",
                    MinThroughputMBps = 5000,
                    MaxThroughputMBps = 10000,
                    IoPattern = "Sequential"
                },
                AnfSuitabilityInfo = new AnfSuitability
                {
                    Compatible = true,
                    RecommendedServiceLevel = "Ultra",
                    Notes = "ANF scales well for parallel workloads. Consider throughput limits per volume. May need multiple volumes for scale-out.",
                    Caveats = new[] { "Throughput more critical than IOPS", "May require volume striping for maximum performance" }
                },
                Hints = new DetectionHints
                {
                    NamingPatterns = new[] { "hpc", "scratch", "compute", "simulation", "cluster", "mpi", "rendering", "genomics" },
                    CommonTags = new[] { "HPC", "HighPerformanceComputing", "Cluster", "Simulation", "Rendering", "AI-Training" },
                    FileTypeIndicators = Array.Empty<string>(),
                    PathPatterns = Array.Empty<string>()
                }
            },
            
            // 8. General File Share
            new WorkloadProfile
            {
                RowKey = "general-fileshare-profile",
                Name = "General File Share",
                Description = "Standard file shares for departmental storage, user home directories, shared documents, or general-purpose file storage. Moderate performance requirements. Common use cases include team collaboration, document management, or simple file storage. Not mission-critical applications. SMB protocol typical. Mixed file types and access patterns.",
                IsSystemProfile = true,
                IsExclusionProfile = false,
                CreatedAt = DateTime.UtcNow,
                PerformanceRequirements = new PerformanceRequirements
                {
                    MinSizeGB = 100,
                    MaxSizeGB = 102400, // 100 TB
                    MinIops = 100,
                    MaxIops = 5000,
                    LatencySensitivity = "Low",
                    MinThroughputMBps = 10,
                    MaxThroughputMBps = 500,
                    IoPattern = "Random"
                },
                AnfSuitabilityInfo = new AnfSuitability
                {
                    Compatible = true,
                    RecommendedServiceLevel = "Standard",
                    Notes = "Cost-benefit analysis needed. May not justify migration if performance adequate and cost higher in ANF.",
                    Caveats = new[] { "Evaluate cost vs. Azure Files pricing", "Premium tier may not be necessary" }
                },
                Hints = new DetectionHints
                {
                    NamingPatterns = new[] { "shared", "fileshare", "department", "team", "documents", "home", "data", "files" },
                    CommonTags = new[] { "FileShare", "Shared", "Documents", "Department", "General" },
                    FileTypeIndicators = Array.Empty<string>(),
                    PathPatterns = Array.Empty<string>()
                }
            },
            
            // 9. Azure VMware Solution (AVS) Datastore
            new WorkloadProfile
            {
                RowKey = "avs-datastore-profile",
                Name = "Azure VMware Solution (AVS) Datastore",
                Description = "NFS datastores for Azure VMware Solution providing additional storage for VMware VMs beyond vSAN. Used for VM storage, ISO libraries, backup repositories, or vSAN capacity expansion. Requires VMware compatibility (NFS 3.0 or 4.1). Performance needs vary based on VM workloads hosted. Mission-critical for AVS environments. Must support VMware storage APIs.",
                IsSystemProfile = true,
                IsExclusionProfile = false,
                CreatedAt = DateTime.UtcNow,
                PerformanceRequirements = new PerformanceRequirements
                {
                    MinSizeGB = 4096, // 4 TB
                    MaxSizeGB = 102400, // 100 TB
                    MinIops = 10000,
                    MaxIops = 50000,
                    LatencySensitivity = "High",
                    MinThroughputMBps = 500,
                    MaxThroughputMBps = 4000,
                    IoPattern = "Mixed"
                },
                AnfSuitabilityInfo = new AnfSuitability
                {
                    Compatible = true,
                    RecommendedServiceLevel = "Premium",
                    Notes = "ANF is a recommended storage option for AVS. Supports VMware features like snapshots and clones. Verify network connectivity between AVS and ANF.",
                    Caveats = new[] { "Requires NFSv3 or NFSv4.1", "Check AVS-ANF network connectivity", "Ultra tier for performance-critical VMs" }
                },
                Hints = new DetectionHints
                {
                    NamingPatterns = new[] { "avs", "vmware", "datastore", "vcenter", "esxi", "vm-storage", "vsphere" },
                    CommonTags = new[] { "AVS", "VMware", "AzureVMwareSolution", "Datastore", "vSphere", "ESXi" },
                    FileTypeIndicators = Array.Empty<string>(),
                    PathPatterns = Array.Empty<string>()
                }
            }
        };
    }

    private TableEntity ConvertToTableEntity(WorkloadProfile profile)
    {
        return new TableEntity(profile.PartitionKey, profile.RowKey)
        {
            ["Name"] = profile.Name,
            ["Description"] = profile.Description,
            ["IsSystemProfile"] = profile.IsSystemProfile,
            ["IsExclusionProfile"] = profile.IsExclusionProfile,
            ["CreatedAt"] = profile.CreatedAt,
            ["UpdatedAt"] = profile.UpdatedAt,
            ["PerformanceRequirementsJson"] = profile.PerformanceRequirementsJson,
            ["AnfSuitabilityJson"] = profile.AnfSuitabilityJson,
            ["DetectionHintsJson"] = profile.DetectionHintsJson
        };
    }

    private WorkloadProfile ConvertFromTableEntity(TableEntity entity)
    {
        return new WorkloadProfile
        {
            PartitionKey = entity.PartitionKey,
            RowKey = entity.RowKey,
            Timestamp = entity.Timestamp,
            ETag = entity.ETag,
            Name = entity.GetString("Name") ?? "",
            Description = entity.GetString("Description") ?? "",
            IsSystemProfile = entity.GetBoolean("IsSystemProfile") ?? false,
            IsExclusionProfile = entity.GetBoolean("IsExclusionProfile") ?? false,
            CreatedAt = entity.GetDateTimeOffset("CreatedAt")?.UtcDateTime ?? DateTime.UtcNow,
            UpdatedAt = entity.GetDateTimeOffset("UpdatedAt")?.UtcDateTime,
            PerformanceRequirementsJson = entity.GetString("PerformanceRequirementsJson"),
            AnfSuitabilityJson = entity.GetString("AnfSuitabilityJson"),
            DetectionHintsJson = entity.GetString("DetectionHintsJson")
        };
    }
}
