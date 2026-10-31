#!/usr/bin/env bash
# 项目构建环境（bash / Git Bash 用）
# 用法：source build/env.sh
#
# 说明：这台机器的 %TEMP% 和 D:\ 根目录都被系统权限限制，无法直接写入，
# 所以把 SDK、NuGet 缓存、临时文件、dotnet 用户目录全部放在项目内的 .tools 下。

export DOTNET_ROOT='D:\focustool\.tools\dotnet'
export PATH="/d/focustool/.tools/dotnet:$PATH"
export NUGET_PACKAGES='D:\focustool\.tools\nuget-packages'
export NUGET_HTTP_CACHE_PATH='D:\focustool\.tools\nuget-http-cache'
export TEMP='D:\focustool\.tools\temp'
export TMP='D:\focustool\.tools\temp'
export DOTNET_CLI_HOME='D:\focustool\.tools\dotnet-home'
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
