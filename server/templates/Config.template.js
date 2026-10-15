// Lifestone ACE server configuration (template).
// setup-server.ps1 renders this to server/runtime/ace/Config.js (gitignored; it contains the DB password).
// Placeholders are replaced with JSON-escaped values: {{WORLD_NAME}} {{HOST}} {{PORT}} {{DAT_DIR}} {{MODS_DIR}}
// {{DB_HOST}} {{DB_PORT}} {{DB_USER}} {{DB_PASSWORD}}.
// SECURITY: Network.Host must stay 127.0.0.1. ACE falls back to 0.0.0.0 (all interfaces) if the value does not parse,
// and start-server.ps1 refuses to launch ACE unless the rendered value is exactly 127.0.0.1.
{
  "Server": {
    "WorldName": {{WORLD_NAME}},

    "Network": {
      // Loopback only. ACE also binds Port+1 (9001) for server-to-client traffic.
      "Host": {{HOST}},
      "Port": {{PORT}},
      "MaximumAllowedSessions": 128,
      "DefaultSessionTimeout": 60,
      "MaximumAllowedSessionsPerIPAddress": -1,
      "AllowUnlimitedSessionsFromIPAddresses": []
    },

    "Accounts": {
      "OverrideCharacterPermissions": true,
      "DefaultAccessLevel": 0,
      // Single-player: the client's first login creates the account, as a Player (DefaultAccessLevel 0). ACE's own
      // rule would make it an Admin when the auth DB has no Admin yet; setup's sentinel Admin account and the
      // Lifestone server mod (server/mod, which refuses to load unless DefaultAccessLevel is 0) keep that from happening.
      "AllowAutoAccountCreation": true,
      "PasswordHashWorkFactor": 8,
      "ForceWorkFactorMigration": true
    },

    // Retail end-of-retail DAT folder (client_cell_1.dat, client_portal.dat, client_local_English.dat;
    // client_highres.dat is optional for ACE).
    "DatFilesDirectory": {{DAT_DIR}},

    // Harmony mods folder (ACE creates it if missing, so it must point inside server/runtime).
    "ModsDirectory": {{MODS_DIR}},

    "ShutdownInterval": "60",
    "ServerPerformanceMonitorAutoStart": false,

    "Threading": {
      "WorldThreadCountMultiplier": 0.34,
      "DatabaseThreadCountMultiplier": 0.66,
      "MultiThreadedLandblockGroupPhysicsTicking": true,
      "MultiThreadedLandblockGroupTicking": true
    },

    "ShardPlayerBiotaCacheTime": "31",
    "ShardNonPlayerBiotaCacheTime": "11",
    "WorldDatabasePrecaching": false,
    "LandblockPreloading": true,
    "PreloadedLandblocks": [
      { "Id": "E74EFFFF", "Description": "Hebian-To (Global Events)", "Permaload": true, "IncludeAdjacents": false, "Enabled": true },
      { "Id": "A9B4FFFF", "Description": "Holtburg", "Permaload": true, "IncludeAdjacents": true, "Enabled": false },
      { "Id": "DA55FFFF", "Description": "Shoushi", "Permaload": true, "IncludeAdjacents": true, "Enabled": false },
      { "Id": "7D64FFFF", "Description": "Yaraq", "Permaload": true, "IncludeAdjacents": true, "Enabled": false },
      { "Id": "0007FFFF", "Description": "Town Network", "Permaload": true, "IncludeAdjacents": false, "Enabled": false },
      { "Id": "00000000", "Description": "Apartment Landblocks", "Permaload": true, "IncludeAdjacents": false, "Enabled": false }
    ]
  },

  "MySql": {
    "Authentication": {
      "Host": {{DB_HOST}}, "Port": {{DB_PORT}}, "Database": "ace_auth",
      "Username": {{DB_USER}}, "Password": {{DB_PASSWORD}},
      "EnableDetailedErrors": false, "EnableSensitiveDataLogging": false
    },
    "Shard": {
      "Host": {{DB_HOST}}, "Port": {{DB_PORT}}, "Database": "ace_shard",
      "Username": {{DB_USER}}, "Password": {{DB_PASSWORD}},
      "EnableDetailedErrors": false, "EnableSensitiveDataLogging": false
    },
    "World": {
      "Host": {{DB_HOST}}, "Port": {{DB_PORT}}, "Database": "ace_world",
      "Username": {{DB_USER}}, "Password": {{DB_PASSWORD}},
      "EnableDetailedErrors": false, "EnableSensitiveDataLogging": false
    }
  },

  "Offline": {
    "PurgeDeletedCharacters": false,
    "PurgeDeletedCharactersDays": 30,
    "PurgeOrphanedBiotas": false,
    "PruneDeletedCharactersFromFriendLists": true,
    "PruneDeletedObjectsFromShortcutBars": false,
    "PruneDeletedCharactersFromSquelchLists": false,

    // Pinned versions: no GitHub API calls at boot.
    "AutoServerUpdateCheck": false,
    "AutoUpdateWorldDatabase": false,
    "AutoApplyWorldCustomizations": false,
    "WorldCustomizationAddedPaths": [],
    "RecurseWorldCustomizationPaths": false,

    // Local only: applies ACE's bundled Database/Updates scripts that are not yet listed in
    // DatabaseSetupScripts/Updates/*/applied_updates.txt. setup-server.ps1 pre-applies them, so this is a no-op
    // unless ACE itself is upgraded.
    "AutoApplyDatabaseUpdates": true
  },

  "DDD": {
    "EnableDATPatching": false,
    "PrecacheCompressedDATFiles": false
  }
}
