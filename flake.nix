{
  description = "Dotnet 7 development environment";
  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-23.11";
    flake-utils.url = "github:numtide/flake-utils";
  };
  outputs = { self, nixpkgs, flake-utils }:
    flake-utils.lib.eachDefaultSystem (system:
      let
        pkgs = nixpkgs.legacyPackages.${system};
      in
      {
        devShells.default = pkgs.mkShell {
          packages = with pkgs; [
            dotnet-sdk_7
            dotnet-runtime_7
            dotnetPackages.Nuget
          ];
          shellHook = ''
            export DOTNET_CLI_TELEMETRY_OPTOUT=1
            echo "Dotnet 7 development environment activated!"
            echo "SDK Version: $(dotnet --version)"
            echo "Telemetry Status: $(
                if [ "$DOTNET_CLI_TELEMETRY_OPTOUT" = "1" ]; then echo "Disabled ✓"; 
                else echo "Enabled ✗"; fi
            )"
          '';
        };
      });
}
