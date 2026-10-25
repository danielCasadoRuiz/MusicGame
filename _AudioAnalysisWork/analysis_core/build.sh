#!/bin/sh
# Builds the harness (core + harness sources) with Unity's bundled Roslyn against the .NET 8 runtime.
set -e
cd "$(dirname "$0")"
FW="C:/Program Files/dotnet/shared/Microsoft.NETCore.App/8.0.11"
CSC="C:/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Data/DotNetSdkRoslyn/csc.dll"
RSP=build.rsp
: > $RSP
for f in "$FW"/*.dll; do
  b=$(basename "$f")
  case "$b" in
    *.Native.dll) ;;
    System.*|Microsoft.CSharp.dll|Microsoft.VisualBasic*.dll|Microsoft.Win32.*|mscorlib.dll|netstandard.dll|WindowsBase.dll) echo "-reference:\"$f\"" >> $RSP;;
  esac
done
mkdir -p bin
"C:/Program Files/dotnet/dotnet.exe" "$CSC" -nologo -noconfig -nostdlib -langversion:9.0 -optimize+ -unsafe- -nullable:disable -warn:4 -target:exe -out:bin/harness.dll @$RSP Core/*.cs Harness/*.cs
cat > bin/harness.runtimeconfig.json <<'J'
{ "runtimeOptions": { "tfm": "net8.0", "framework": { "name": "Microsoft.NETCore.App", "version": "8.0.11" },
  "configProperties": { "System.GC.Server": false, "System.GC.Concurrent": true } } }
J
echo BUILD OK
