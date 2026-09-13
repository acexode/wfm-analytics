#!/usr/bin/env bash
# Source this file from another project script. No values are printed.
WFM_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false
export DOTNET_CLI_HOME="$WFM_ROOT/.local/dotnet-home"
export NUGET_PACKAGES="$WFM_ROOT/.local/nuget"
export NUGET_CONFIG_FILE="$WFM_ROOT/NuGet.Config"
if [[ -x "$WFM_ROOT/.local/dotnet/dotnet" ]]; then
  export DOTNET_ROOT="$WFM_ROOT/.local/dotnet"
  export PATH="$DOTNET_ROOT:$PATH"
elif [[ -x "/c/Program Files/dotnet/dotnet" ]]; then
  export DOTNET_ROOT="/c/Program Files/dotnet"
  export PATH="$DOTNET_ROOT:$PATH"
elif [[ -x "/mnt/c/Program Files/dotnet/dotnet" ]]; then
  export DOTNET_ROOT="/mnt/c/Program Files/dotnet"
  export PATH="$DOTNET_ROOT:$PATH"
fi
WFM_BUNDLED_NODE_DIR="/Users/Sir Abubakar/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin"
if [[ -x "$WFM_BUNDLED_NODE_DIR/node" ]]; then
  export PATH="$WFM_BUNDLED_NODE_DIR:$PATH"
fi
if [[ -f "$WFM_ROOT/.local/database.env" ]]; then
  source "$WFM_ROOT/.local/database.env"
  export ConnectionStrings__Migration="Host=127.0.0.1;Port=55432;Database=wfm_dev;Username=wfm_migrator;Password=$WFM_MIGRATION_PASSWORD"
  export ConnectionStrings__Primary="Host=127.0.0.1;Port=55432;Database=wfm_dev;Username=wfm_app;Password=$WFM_APP_PASSWORD"
  export WFM_TEST_DATABASE="Host=127.0.0.1;Port=55432;Database=wfm_test;Username=wfm_migrator;Password=$WFM_MIGRATION_PASSWORD"
fi
