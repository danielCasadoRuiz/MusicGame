#!/bin/sh
# Compiles ONLY Core/*.cs against Unity 6000.3.2f1's .NET Standard 2.1 reference (C# 9), i.e. the API
# surface the files will see once copied into Assets/_Project/Scripts/Audio/Analysis.
cd "$(dirname "$0")"
NS="C:/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Data/NetStandard/ref/2.1.0/netstandard.dll"
CSC="C:/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Data/DotNetSdkRoslyn/csc.dll"
mkdir -p bin
"C:/Program Files/dotnet/dotnet.exe" "$CSC" -nologo -noconfig -nostdlib -langversion:9.0 -unsafe- -warn:4 -target:library -out:bin/core_netstandard.dll -reference:"$NS" Core/*.cs && echo "NETSTANDARD2.1 BUILD OK"
