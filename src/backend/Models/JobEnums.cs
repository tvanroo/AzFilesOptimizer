namespace AzFilesOptimizer.Backend.Models;

public enum JobType
{
    Discovery,
    Optimization
}

public enum JobStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}
