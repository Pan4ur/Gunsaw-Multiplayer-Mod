@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "GUNSAW_INSTALL_BAT=%~f0"
set "GUNSAW_INSTALL_DIR=%~dp0"
set "GUNSAW_INSTALL_CWD=%CD%"
pushd "%SystemRoot%\System32"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -Command ^
  "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'Continue';" ^
  "$messages = @{}; $language = 2 + [int]((Get-UICulture).TwoLetterISOLanguageName -eq 'ru');" ^
  "foreach ($line in [IO.File]::ReadLines($env:GUNSAW_INSTALL_BAT)) {" ^
  "  if ($line -match '^:text ([^|]+)\|([^|]+)\|(.+)$') { $messages[$Matches[1]] = $Matches[$language] }" ^
  "};" ^
  "function Step($percent, $name) { Write-Progress -Id 1 -Activity $messages.Title -Status $messages[$name] -PercentComplete $percent };" ^
  "function IsGame($path) {" ^
  "  $path -and [IO.File]::Exists([IO.Path]::Combine($path, 'Gunsaw.exe')) -and [IO.Directory]::Exists([IO.Path]::Combine($path, 'Gunsaw_Data'))" ^
  "};" ^
  "function Download($url, $path, $size, $digest) {" ^
  "  if ($digest -notmatch '^sha256:[a-f0-9]{64}$') { throw $messages.Verify };" ^
  "  Invoke-WebRequest -Uri $url -OutFile $path -UseBasicParsing -TimeoutSec 120;" ^
  "  if ((Get-Item -LiteralPath $path).Length -ne $size -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $digest.Substring(7)) { throw $messages.Verify }" ^
  "};" ^
  "function PluginVersion($path, $dir) {" ^
  "  $resolve = [ResolveEventHandler] {" ^
  "    param($sender, $eventArgs);" ^
  "    $name = ([Reflection.AssemblyName]$eventArgs.Name).Name + '.dll';" ^
  "    foreach ($folder in 'BepInEx\core', 'Gunsaw_Data\Managed') {" ^
  "      $dependency = Join-Path (Join-Path $dir $folder) $name;" ^
  "      if ([IO.File]::Exists($dependency)) { return [Reflection.Assembly]::ReflectionOnlyLoadFrom($dependency) }" ^
  "    };" ^
  "    return [Reflection.Assembly]::ReflectionOnlyLoad($eventArgs.Name)" ^
  "  };" ^
  "  [AppDomain]::CurrentDomain.add_ReflectionOnlyAssemblyResolve($resolve);" ^
  "  try {" ^
  "    $assembly = [Reflection.Assembly]::ReflectionOnlyLoad([IO.File]::ReadAllBytes($path));" ^
  "    return [version]$assembly.GetType('GunsawMultiplayerPlugin', $true).GetField('PluginVersion').GetRawConstantValue()" ^
  "  } catch { return $null } finally { [AppDomain]::CurrentDomain.remove_ReflectionOnlyAssemblyResolve($resolve) }" ^
  "};" ^
  "$stage = $null; $exitCode = 0;" ^
  "try {" ^
  "  Write-Host $messages.Title;" ^
  "  $game = @($env:GUNSAW_INSTALL_DIR, $env:GUNSAW_INSTALL_CWD, 'C:\Games\Gunsaw Demo') | Where-Object { IsGame $_ } | Select-Object -First 1;" ^
  "  while (-not $game) {" ^
  "    $inputPath = (Read-Host $messages.Find).Trim().Trim([char]34);" ^
  "    if (-not $inputPath) { exit 0 };" ^
  "    try {" ^
  "      $inputPath = [IO.Path]::GetFullPath([IO.Path]::Combine($env:GUNSAW_INSTALL_CWD, $inputPath));" ^
  "      if ([IO.Path]::GetFileName($inputPath) -eq 'Gunsaw.exe') { $inputPath = [IO.Path]::GetDirectoryName($inputPath) }" ^
  "    } catch { Write-Host $messages.InvalidPath; continue };" ^
  "    if (IsGame $inputPath) { $game = $inputPath } else { Write-Host $messages.InvalidPath }" ^
  "  };" ^
  "  $game = (Get-Item -LiteralPath $game).FullName;" ^
  "  $exe = Join-Path $game 'Gunsaw.exe';" ^
  "  Write-Host $game;" ^
  "  if (Get-Process -Name Gunsaw -ErrorAction SilentlyContinue) { throw $messages.Running };" ^
  "  [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12;" ^
  "  Step 10 Check;" ^
  "  $repo = 'Pan4ur/Gunsaw-Multiplayer-Mod';" ^
  "  $release = Invoke-RestMethod -Uri ('https://api.github.com/repos/' + $repo + '/releases/latest') -TimeoutSec 30;" ^
  "  $tag = $release.tag_name;" ^
  "  $asset = $release.assets | Where-Object { $_.name -eq 'GunsawMultiplayer.dll' } | Select-Object -First 1;" ^
  "  if (-not $asset -or $tag -notmatch '^v?\d+(\.\d+){1,3}$') { throw $messages.Release };" ^
  "  $latest = [version]($tag -replace '^v', '');" ^
  "  $plugins = Join-Path $game 'BepInEx\plugins';" ^
  "  $mod = Join-Path $plugins $asset.name;" ^
  "  $core = Join-Path $game 'BepInEx\core\BepInEx.dll';" ^
  "  $bootFiles = @('winhttp.dll', 'doorstop_config.ini');" ^
  "  $needsLoader = -not [IO.File]::Exists($core) -or ($bootFiles | Where-Object { -not [IO.File]::Exists((Join-Path $game $_)) });" ^
  "  $installed = $null; $needsMod = $true;" ^
  "  if ([IO.File]::Exists($mod)) {" ^
  "    if (('sha256:' + (Get-FileHash -LiteralPath $mod).Hash) -eq $asset.digest) { $needsMod = $false }" ^
  "    else { $installed = PluginVersion $mod $game; $needsMod = -not $installed -or $installed -lt $latest }" ^
  "  };" ^
  "  if ($needsLoader -or $needsMod) {" ^
  "    $stage = Join-Path $game ('.gunsaw-install-' + [guid]::NewGuid());" ^
  "    [IO.Directory]::CreateDirectory($stage) | Out-Null;" ^
  "  };" ^
  "  if ($needsLoader) {" ^
  "    $reader = New-Object IO.BinaryReader ([IO.File]::OpenRead($exe));" ^
  "    try {" ^
  "      if ($reader.ReadUInt16() -ne 0x5A4D) { throw $messages.Arch };" ^
  "      $reader.BaseStream.Position = 0x3C;" ^
  "      $reader.BaseStream.Position = $reader.ReadUInt32();" ^
  "      if ($reader.ReadUInt32() -ne 0x4550) { throw $messages.Arch };" ^
  "      $arch = @{ 332 = 'x86'; 34404 = 'x64' }[[int]$reader.ReadUInt16()];" ^
  "      if (-not $arch) { throw $messages.Arch }" ^
  "    } finally { $reader.Dispose() };" ^
  "    Step 25 LoaderDownload;" ^
  "    $bepVersion = '5.4.23.6';" ^
  "    $archive = Join-Path $stage 'BepInEx.zip';" ^
  "    $url = 'https://github.com/BepInEx/BepInEx/releases/download/v{0}/BepInEx_win_{1}_{0}.zip' -f $bepVersion, $arch;" ^
  "    $checksums = @{ x64 = 'sha256:6c57a714a892efb4e9e1fef63d608a3391f30ec984031c465404e4ee381a0755'; x86 = 'sha256:a4024dafaca5d3784097e34b3c2921415030c0b461e52f30053ed8914be9622d' };" ^
  "    Download $url $archive (@{ x64 = 642076; x86 = 641457 }[$arch]) $checksums[$arch];" ^
  "    $loader = Join-Path $stage 'loader';" ^
  "    Expand-Archive -LiteralPath $archive -DestinationPath $loader;" ^
  "  };" ^
  "  if ($needsMod) {" ^
  "    Step 55 ModDownload;" ^
  "    $dll = Join-Path $stage $asset.name;" ^
  "    Download ('https://github.com/{0}/releases/download/{1}/{2}' -f $repo, $tag, $asset.name) $dll $asset.size $asset.digest;" ^
  "  };" ^
  "  if (Get-Process -Name Gunsaw -ErrorAction SilentlyContinue) { throw $messages.Running };" ^
  "  Step 80 Install;" ^
  "  if ($needsLoader) {" ^
  "    foreach ($file in Get-ChildItem -LiteralPath $loader -File -Recurse -Force) {" ^
  "      $target = Join-Path $game $file.FullName.Substring($loader.Length + 1);" ^
  "      [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null;" ^
  "      if (-not [IO.File]::Exists($target)) { [IO.File]::Move($file.FullName, $target) }" ^
  "    }" ^
  "  };" ^
  "  [IO.Directory]::CreateDirectory($plugins) | Out-Null;" ^
  "  if ($needsMod) {" ^
  "    if ([IO.File]::Exists($mod)) { [IO.File]::Replace($dll, $mod, (Join-Path $stage 'previous.dll')) }" ^
  "    else { [IO.File]::Move($dll, $mod) }" ^
  "  };" ^
  "  Step 100 Done;" ^
  "  Write-Progress -Id 1 -Activity $messages.Title -Completed;" ^
  "  if ($installed -gt $latest) { Write-Host ($messages.Newer -f $installed) }" ^
  "  elseif ($needsMod) { Write-Host ($messages.Done -f $tag) }" ^
  "  else { Write-Host ($messages.Current -f $tag) };" ^
  "  do { $choice = Read-Host $messages.Choice } until ($choice -in '1', '2', '');" ^
  "  if ($choice -eq '2') { Start-Process -FilePath $exe -WorkingDirectory $game }" ^
  "} catch {" ^
  "  Write-Progress -Id 1 -Activity $messages.Title -Completed;" ^
  "  Write-Host ($messages.Failed -f $_.Exception.Message) -ForegroundColor Red;" ^
  "  $null = Read-Host $messages.Close; $exitCode = 1" ^
  "} finally {" ^
  "  if ($stage -and [IO.Directory]::Exists($stage)) { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }" ^
  "}; exit $exitCode"
set "GUNSAW_INSTALL_EXIT=%ERRORLEVEL%"
popd
exit /b %GUNSAW_INSTALL_EXIT%

:text Title|Gunsaw Multiplayer installer|Установщик Gunsaw Multiplayer
:text Find|Enter the Gunsaw folder or run this file from that folder. Enter with no path closes the installer|Укажите папку Gunsaw или запустите этот файл из папки игры. Нажмите Enter без пути, чтобы закрыть установщик
:text InvalidPath|This folder does not contain Gunsaw.exe and Gunsaw_Data.|В этой папке нет Gunsaw.exe и Gunsaw_Data.
:text Running|Close Gunsaw and run the installer again.|Закройте Gunsaw и запустите установщик снова.
:text Check|Checking the latest stable release|Проверка последнего стабильного релиза
:text Release|The latest stable release has no usable GunsawMultiplayer.dll or version tag.|В последнем стабильном релизе нет подходящего GunsawMultiplayer.dll или тега версии.
:text Verify|Could not verify the downloaded file against its GitHub release.|Не удалось сверить скачанный файл с данными релиза GitHub.
:text Arch|Could not determine the Windows architecture of Gunsaw.exe.|Не удалось определить разрядность Gunsaw.exe.
:text LoaderDownload|Downloading BepInEx|Загрузка BepInEx
:text ModDownload|Downloading Gunsaw Multiplayer|Загрузка Gunsaw Multiplayer
:text Install|Installing files|Установка файлов
:text Done|Gunsaw Multiplayer {0} is ready to play.|Gunsaw Multiplayer {0} готов к запуску.
:text Current|Gunsaw Multiplayer {0} is already up to date.|Gunsaw Multiplayer {0} уже обновлён до последней версии.
:text Newer|Installed Gunsaw Multiplayer {0} is newer than the latest stable release.|Установленная версия Gunsaw Multiplayer {0} новее последнего стабильного релиза.
:text Choice|[1] Close  [2] Close and launch Gunsaw|[1] Закрыть  [2] Закрыть и запустить Gunsaw
:text Failed|Installation failed. {0}|Не удалось завершить установку. {0}
:text Close|Press Enter to close|Нажмите Enter, чтобы закрыть окно