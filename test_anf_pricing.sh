#!/bin/bash

# Test ANF Flexible pricing API calls for South Central US

echo "=== Testing ANF Flexible Capacity ==="
curl -s "https://prices.azure.com/api/retail/prices?\$filter=serviceFamily%20eq%20'Storage'%20and%20serviceName%20eq%20'Azure%20NetApp%20Files'%20and%20productName%20eq%20'Azure%20NetApp%20Files'%20and%20armRegionName%20eq%20'southcentralus'%20and%20skuName%20eq%20'Flexible%20Service%20Level'%20and%20meterName%20eq%20'Flexible%20Service%20Level%20Capacity'" | jq -r '.Items[] | "Capacity: \(.retailPrice) \(.currencyCode)/\(.unitOfMeasure)"'

echo ""
echo "=== Testing ANF Flexible Throughput ==="
curl -s "https://prices.azure.com/api/retail/prices?\$filter=serviceFamily%20eq%20'Storage'%20and%20serviceName%20eq%20'Azure%20NetApp%20Files'%20and%20productName%20eq%20'Azure%20NetApp%20Files'%20and%20armRegionName%20eq%20'southcentralus'%20and%20skuName%20eq%20'Flexible%20Service%20Level'%20and%20meterName%20eq%20'Flexible%20Service%20Level%20Throughput%20MiBps'" | jq -r '.Items[] | "Throughput: \(.retailPrice) \(.currencyCode)/\(.unitOfMeasure)"'

echo ""
echo "=== Testing ANF Cool Storage ==="
curl -s "https://prices.azure.com/api/retail/prices?\$filter=serviceFamily%20eq%20'Storage'%20and%20serviceName%20eq%20'Azure%20NetApp%20Files'%20and%20productName%20eq%20'Azure%20NetApp%20Files'%20and%20armRegionName%20eq%20'southcentralus'%20and%20skuName%20eq%20'Standard%20Storage%20with%20Cool%20Access'%20and%20meterName%20eq%20'Standard%20Storage%20with%20Cool%20Access%20Capacity'" | jq -r '.Items[] | "Cool Storage: \(.retailPrice) \(.currencyCode)/\(.unitOfMeasure)"'

echo ""
echo "=== Testing ANF Cool Transfer ==="
curl -s "https://prices.azure.com/api/retail/prices?\$filter=serviceFamily%20eq%20'Storage'%20and%20serviceName%20eq%20'Azure%20NetApp%20Files'%20and%20productName%20eq%20'Azure%20NetApp%20Files'%20and%20armRegionName%20eq%20'southcentralus'%20and%20skuName%20eq%20'Standard%20Storage%20with%20Cool%20Access'%20and%20meterName%20eq%20'Standard%20Storage%20with%20Cool%20Access%20Data%20Transfer'" | jq -r '.Items[] | "Cool Transfer: \(.retailPrice) \(.currencyCode)/\(.unitOfMeasure)"'

echo ""
echo "=== Testing what our code queries (all Flexible meters) ==="
curl -s "https://prices.azure.com/api/retail/prices?\$filter=serviceFamily%20eq%20'Storage'%20and%20serviceName%20eq%20'Azure%20NetApp%20Files'%20and%20productName%20eq%20'Azure%20NetApp%20Files'%20and%20armRegionName%20eq%20'southcentralus'%20and%20skuName%20eq%20'Flexible%20Service%20Level'" | jq -r '.Items[] | "\(.meterName): \(.retailPrice) \(.currencyCode)/\(.unitOfMeasure)"'

echo ""
echo "=== Expected Cost Calculation ===" 
echo "Volume: 50 GiB provisioned, 12 MiB/s throughput, pool has 128 MiB/s total"
echo "Data Capacity: 50 GiB * \$0.000181/GiB/Hour * 730 hours = \$6.61"
echo "Throughput: 12 MiB/s is within pool baseline of 128 MiB/s = \$0.00"
echo "Total: \$6.61/month"
