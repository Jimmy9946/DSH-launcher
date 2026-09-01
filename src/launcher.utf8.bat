@echo off
setlocal EnableExtensions EnableDelayedExpansion
chcp 936 >nul 2>&1
title DSH 一键部署启动器

rem ============================================================
rem  DSH 一键部署启动器 v1.0
rem  - 自动检测/下载 Node 免安装版(官方源, 失败自动切国内镜像)
rem  - 自动安装 DSH (@deepseek-ai/dsh)
rem  - 启动 DSH Web 并打开浏览器
rem  - 完全绿色自包含: 所有组件在脚本所在目录 runtime 下
rem  可用环境变量:
rem    DSH_LAUNCHER_NODE_VERSION  指定 Node 版本 (默认 22.23.2)
rem    DSH_LAUNCHER_FORCE_MIRROR  设为 1 强制走国内镜像
rem    DSH_LAUNCHER_PORT          指定 Web 端口 (默认 3080, 勿设 0)
rem    DSH_HOME                  DSH 数据目录 (默认 %USERPROFILE%\.dsh)
rem ============================================================

rem ---------- 配置 ----------
set "NODE_VERSION=%DSH_LAUNCHER_NODE_VERSION%"
if "%NODE_VERSION%"=="" set "NODE_VERSION=22.23.2"
set "NODE_ZIP=node-v%NODE_VERSION%-win-x64.zip"
set "NODE_DIR=node-v%NODE_VERSION%-win-x64"
set "DSH_PKG=@deepseek-ai/dsh"
set "WEB_PORT=%DSH_LAUNCHER_PORT%"
if "%WEB_PORT%"=="" set "WEB_PORT=3080"
set "WEB_URL=http://127.0.0.1:%WEB_PORT%"

set "APP_DIR=%~dp0"
set "RUNTIME_DIR=%APP_DIR%runtime"
set "NODE_HOME=%RUNTIME_DIR%\%NODE_DIR%"
set "NODE_EXE=%NODE_HOME%\node.exe"
set "NPM_CMD=%NODE_HOME%\npm.cmd"
set "LOG_DIR=%APP_DIR%logs"
if not exist "%LOG_DIR%" mkdir "%LOG_DIR%"
set "LOG_FILE=%LOG_DIR%\launcher.log"
echo ===== %date% %time% ===== >> "%LOG_FILE%"

set "USED_NODE="
set "USED_NPM="
set "DSH_CMD="
set "REGISTRY="

echo ================================================
echo    DSH 一键部署启动器
echo    版本: v1.0   目标 Node: %NODE_VERSION%
echo ================================================
echo.

rem ---------- 1/3 检测 Node ----------
echo [1/3] 检测 Node 运行环境...
call :step_check_node
if errorlevel 1 goto :failed

rem ---------- 2/3 安装 DSH ----------
echo.
call :step_install_dsh
if errorlevel 1 goto :failed

rem ---------- 3/3 启动 DSH Web ----------
echo.
call :step_start_web
if errorlevel 1 goto :failed

echo.
echo ================================================
echo  部署完成! DSH Web: %WEB_URL%
echo   - DSH 数据目录: %DSH_HOME%
echo   - 浏览器未自动打开时可手动访问 %WEB_URL%
echo   - 关闭 "DSH Web" 窗口即停止服务
echo   - 以后再次双击本启动器即可直接启动
echo ================================================
echo. >> "%LOG_FILE%"
pause
exit /b 0

:failed
echo.
echo  [错误] 部署失败, 请检查网络后重试。
echo  详细日志: %LOG_FILE%
echo. >> "%LOG_FILE%"
pause
exit /b 1

rem ============================================================
rem  子程序: 检测 Node
rem  一律使用本启动器 runtime 内置 Node(自包含, 免管理员);
rem  已有且合格则直接使用, 否则下载免安装版
rem ============================================================
:step_check_node
if exist "%NODE_EXE%" (
    call :check_node_version "%NODE_EXE%"
    if not errorlevel 1 (
        echo   使用内置 Node: %NODE_EXE%
        echo   版本: !NODE_VER!
        echo   [launcher] use builtin node !NODE_VER! >> "%LOG_FILE%"
        set "USED_NODE=%NODE_EXE%"
        set "USED_NPM=%NPM_CMD%"
        set "DSH_CMD=%NODE_HOME%\dsh.cmd"
        exit /b 0
    )
    echo   内置 Node 版本异常[!NODE_VER!], 重新下载...
)

echo   未找到内置 Node, 开始下载免安装版...
call :download_node
if errorlevel 1 exit /b 1
set "USED_NODE=%NODE_EXE%"
set "USED_NPM=%NPM_CMD%"
set "DSH_CMD=%NODE_HOME%\dsh.cmd"
exit /b 0

rem ---------- 子程序: 检查 node 版本 (参数: node 路径) ----------
:check_node_version
set "NODE_VER="
for /f "delims=" %%v in ('"%~1" -v 2^>nul') do set "NODE_VER=%%v"
if "%NODE_VER%"=="" exit /b 1
set "NODE_VER=%NODE_VER:v=%"
for /f "tokens=1 delims=." %%m in ("%NODE_VER%") do set "NODE_MAJOR=%%m"
if %NODE_MAJOR% GEQ 22 exit /b 0
exit /b 1

rem ---------- 子程序: 下载免安装 Node ----------
:download_node
echo   下载 Node v%NODE_VERSION% (约 35MB)...
set "NODE_URL_O=https://nodejs.org/dist/v%NODE_VERSION%/%NODE_ZIP%"
set "NODE_URL_M=https://registry.npmmirror.com/-/binary/node/v%NODE_VERSION%/%NODE_ZIP%"
if not exist "%RUNTIME_DIR%" mkdir "%RUNTIME_DIR%"
set "ZIP_FILE=%RUNTIME_DIR%\%NODE_ZIP%"
set "DOWNLOAD_OK="

if "%DSH_LAUNCHER_FORCE_MIRROR%"=="1" goto :dl_mirror
echo   通道: 官方 nodejs.org
call :download_file "%NODE_URL_O%" "%ZIP_FILE%"
if not errorlevel 1 set "DOWNLOAD_OK=1"
if "%DOWNLOAD_OK%"=="" (
    echo   官方通道失败, 切换国内镜像...
    echo   [launcher] official node download failed, switch to mirror >> "%LOG_FILE%"
    call :download_file "%NODE_URL_M%" "%ZIP_FILE%"
    if not errorlevel 1 set "DOWNLOAD_OK=1"
)
goto :dl_done

:dl_mirror
echo   通道: 国内镜像 npmmirror
call :download_file "%NODE_URL_M%" "%ZIP_FILE%"
if not errorlevel 1 set "DOWNLOAD_OK=1"

:dl_done
if "%DOWNLOAD_OK%"=="" (
    echo   [错误] Node 下载失败, 请检查网络后重试
    echo   [launcher] node download failed >> "%LOG_FILE%"
    exit /b 1
)
echo   下载完成, 正在解压...
powershell -NoProfile -Command "$ProgressPreference='SilentlyContinue'; Expand-Archive -LiteralPath '%ZIP_FILE%' -DestinationPath '%RUNTIME_DIR%' -Force"
if errorlevel 1 (
    echo   [错误] 解压失败
    exit /b 1
)
if not exist "%NODE_EXE%" (
    echo   [错误] 解压后未找到 node.exe
    exit /b 1
)
del /q "%ZIP_FILE%" >nul 2>&1
echo   [launcher] node %NODE_VERSION% extracted to %NODE_HOME% >> "%LOG_FILE%"
exit /b 0

rem ---------- 子程序: 下载文件 (参数: url, 输出文件) ----------
:download_file
where curl.exe >nul 2>&1
if not errorlevel 1 (
    curl.exe -L --fail --connect-timeout 15 --retry 2 -o "%~2" "%~1" >nul 2>&1
    if not errorlevel 1 if exist "%~2" exit /b 0
)
powershell -NoProfile -Command "$ProgressPreference='SilentlyContinue'; try { Invoke-WebRequest -Uri '%~1' -OutFile '%~2' -UseBasicParsing -TimeoutSec 600; exit 0 } catch { exit 1 }" >nul 2>&1
if not errorlevel 1 exit /b 0
exit /b 1

rem ============================================================
rem  子程序: 安装 / 检查 DSH
rem ============================================================
:step_install_dsh
echo [2/3] 安装 / 检查 DSH (%DSH_PKG%)...
if exist "%DSH_CMD%" (
    call :check_dsh_version "%DSH_CMD%"
    if not errorlevel 1 (
        echo   DSH 已安装: !DSH_VER!
        echo   [launcher] dsh already installed !DSH_VER! >> "%LOG_FILE%"
        exit /b 0
    )
    echo   DSH 版本异常, 重新安装...
)

set "REGISTRY=https://registry.npmjs.org"
if "%DSH_LAUNCHER_FORCE_MIRROR%"=="1" set "REGISTRY=https://registry.npmmirror.com"

echo   安装中 (源: %REGISTRY%)...
echo   [launcher] npm install -g %DSH_PKG% --registry %REGISTRY% >> "%LOG_FILE%"
call "%USED_NPM%" install -g %DSH_PKG% --registry %REGISTRY% --no-fund --no-audit
if errorlevel 1 (
    if "%DSH_LAUNCHER_FORCE_MIRROR%"=="1" (
        echo   [launcher] npm install failed [mirror] >> "%LOG_FILE%"
        exit /b 1
    )
    echo   官方源失败, 切换国内镜像重试...
    echo   [launcher] npm install failed, switch to mirror >> "%LOG_FILE%"
    set "REGISTRY=https://registry.npmmirror.com"
    call "%USED_NPM%" install -g %DSH_PKG% --registry %REGISTRY% --no-fund --no-audit
    if errorlevel 1 (
        echo   [错误] DSH 安装失败
        echo   [launcher] npm install failed [mirror] >> "%LOG_FILE%"
        exit /b 1
    )
)

call :check_dsh_version "%DSH_CMD%"
if errorlevel 1 (
    echo   [错误] DSH 安装后校验失败
    exit /b 1
)
echo   DSH 安装完成: %DSH_VER%
echo   [launcher] dsh installed %DSH_VER% >> "%LOG_FILE%"
exit /b 0

rem ---------- 子程序: 检查 dsh 版本 (参数: dsh.cmd 路径) ----------
:check_dsh_version
set "DSH_VER="
for /f "delims=" %%v in ('"%~1" --version 2^>nul') do set "DSH_VER=%%v"
if "%DSH_VER%"=="" exit /b 1
exit /b 0

rem ============================================================
rem  子程序: 启动 DSH Web
rem ============================================================
:step_start_web
echo [3/3] 启动 DSH Web 服务...
if not defined DSH_HOME set "DSH_HOME=%USERPROFILE%\.dsh"
echo   DSH 数据目录: %DSH_HOME%

set "WORKSPACE=%USERPROFILE%\DSH-Workspace"
if not exist "%WORKSPACE%" mkdir "%WORKSPACE%"
cd /d "%WORKSPACE%"

if "%DSH_LAUNCHER_FORCE_MIRROR%"=="1" (
    set "npm_config_registry=https://registry.npmmirror.com"
)

rem 端口已被占用则视为服务已在运行
powershell -NoProfile -Command "$c=New-Object Net.Sockets.TcpClient; try{$c.Connect('127.0.0.1',%WEB_PORT%);'OK'}catch{'NO'}" | findstr /C:"OK" >nul 2>&1
if not errorlevel 1 (
    echo   检测到服务已在运行, 直接打开页面
    goto :open_browser
)

echo   正在启动 (首次启动需下载组件, 可能需几分钟)...
echo   [launcher] start: %DSH_CMD% web --port %WEB_PORT% >> "%LOG_FILE%"
start "DSH Web" cmd /k ""%DSH_CMD%" web --port %WEB_PORT%"

set /a tries=0
:wait_port
set /a tries+=1
if %tries% GTR 180 (
    echo   [错误] 等待服务超时[6分钟], 请查看 "DSH Web" 窗口中的日志
    echo   [launcher] web wait timeout >> "%LOG_FILE%"
    exit /b 1
)
powershell -NoProfile -Command "$c=New-Object Net.Sockets.TcpClient; try{$c.Connect('127.0.0.1',%WEB_PORT%);'OK'}catch{'NO'}" | findstr /C:"OK" >nul 2>&1
if not errorlevel 1 goto :port_ok
timeout /t 2 /nobreak >nul
goto wait_port

:port_ok
echo   服务已就绪 (%WEB_URL%)
echo   [launcher] web ready at %WEB_URL% >> "%LOG_FILE%"

:open_browser
start "" "%WEB_URL%"
exit /b 0
