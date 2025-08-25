# Linera Orchestrator

This orchestrator automates the deployment workflow for applications on the **Linera testnet**, including:
- Initializing a local network (start net)
- Publishing the `Leaderboard` module
- Publishing the `XFighter` module
- Creating the `XFighter` application linked to the `Leaderboard`

## Deployment Workflow

1. **Start the Network**
   - Run the orchestrator (`dotnet run`) to initialize a temporary testnet.
   - The wallet and storage files are generated in `/tmp/`.

2. **Publish Leaderboard Module**
   - Deploy the leaderboard module and create the `Leaderboard App ID`.
   - This ID is used later when creating dependent applications.

3. **Publish XFighter Module**
   - Deploy the `XFighter` contract and service module.
   - Capture the `Module ID` from the CLI output.

4. **Create XFighter Application**
   - Use `publish-and-create` to create an app from the published module.
   - The `Leaderboard App ID` is passed as a JSON parameter.

## Storing Application and Module IDs

- IDs are printed in the orchestrator logs (`dotnet run`).
- For better persistence, store them in a separate file: `deployment_ids.txt` or `app_ids.json`.
- Example:

Leaderboard App ID: daffabd7444d52085460fa54e59da74cf97a42d4bab6bbcee2f12dbb002a6818
XFighter Module ID: 38273efe5e4c3db9f2a6bef7436b00ff1f9dee44d8c2199c80248ffb8b9237d19791343c81d1c2abfdee02764b0cc60df64bff7fefaac03ca60c1f60e9fe484000
XFighter App ID: e1fef2b169618f543b422018b158f958ff0c38433992a88d130aecc6e915ad35
