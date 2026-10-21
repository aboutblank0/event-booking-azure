// Names and regions of the live resources. Nothing secret belongs in this file.
using 'main.bicep'

param appLocation = 'ukwest'
param sqlLocation = 'swedencentral'
param storageLocation = 'uksouth'

param planName = 'FreePlan'
param appName = 'jc-eventbooking'
param sqlServerName = 'eventbooking-db'
param sqlDatabaseName = 'free-sql-db-0223583'
param storageAccountName = 'jceventbooking'
param emailServiceName = 'jc-eventbooking-email'
param communicationServiceName = 'jc-eventbooking-acs'

// Read from your shell so your email isn't committed: export ADMIN_EMAIL=you@example.com
param adminEmail = readEnvironmentVariable('ADMIN_EMAIL')
