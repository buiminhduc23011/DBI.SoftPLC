# DBI.Studio quick guide

1. Create or open a `.dbiproj` project.
2. Add devices in **Device Configuration**, then define tags in **PLC Tags**.
3. Open a block and use Roslyn IntelliSense on `IO.<tag>`.
4. Compile and deploy to the separate Runtime process.
5. Open a Watch Table and enable **Monitor** to subscribe only to its tags.
6. Use Force I/O only after confirming the machine area is safe; clear forces before maintenance.

The Runtime remains independent when Studio closes. Monitoring and force state are runtime concerns and are not stored in the project file.
