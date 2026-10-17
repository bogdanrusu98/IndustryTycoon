param([ValidateSet('editmode','m18','m17','gameplay','scene-audit','android-release')][string]$StartAt = 'editmode')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.5.9f1/Editor/Unity.exe'
$jobs = @(
    # AnimatedProductionTests compares rendered pixels, so this suite needs a graphics device.
    @{ name='editmode'; args='-runTests -testPlatform EditMode -testResults "'+$root+'/Logs/meshy-editmode.xml"' },
    @{ name='m18'; args='-executeMethod IndustryTycoon.Editor.LumberCampM18PlayModeSmokeTest.RunFromCommandLine' },
    @{ name='m17'; args='-executeMethod IndustryTycoon.Editor.LumberCampM17PlayModeSmokeTest.RunFromCommandLine' },
    @{ name='gameplay'; args='-executeMethod IndustryTycoon.Editor.LumberCampPlayModeSmokeTest.RunFromCommandLine' },
    @{ name='scene-audit'; args='-nographics -executeMethod IndustryTycoon.Editor.LumberCampPrototypeBuilder.ValidateFromCommandLine -quit' },
    @{ name='android-release'; args='-executeMethod IndustryTycoon.Editor.LumberCampPrototypeBuilder.BuildAndroidPlayerFromCommandLine -customBuildPath "'+$root+'/Builds/Android/IndustryTycoon-Playcraft-Release.apk" -quit' }
)
$started = $false
foreach ($job in $jobs) {
    if ($job.name -eq $StartAt) { $started = $true }
    if (-not $started) { continue }
    Write-Output ('START '+$job.name)
    $arguments = '-batchmode -projectPath "'+$root+'" '+$job.args+' -logFile "'+$root+'/Logs/meshy-'+$job.name+'.log"'
    $process = Start-Process -FilePath $unity -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
    Write-Output ($job.name+' exit='+$process.ExitCode)
    if ($process.ExitCode -ne 0) { throw ('Validation failed: '+$job.name) }
}
