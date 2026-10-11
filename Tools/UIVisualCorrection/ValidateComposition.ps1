param([string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/6000.5.9f1/Editor/Unity.exe', [ValidateSet('final-capture','editmode','m18','m17','gameplay','scene-audit','android-release')][string]$StartAt = 'final-capture')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$unity = $UnityPath
$jobs = @(
    @{ name='final-capture'; args='-executeMethod IndustryTycoon.Editor.IndustrialPlaycraftCapture.RebuildAndRun -visualIteration final' },
    @{ name='editmode'; args='-nographics -runTests -testPlatform EditMode -testResults "'+$root+'/Logs/composition-editmode.xml"' },
    @{ name='m18'; args='-executeMethod IndustryTycoon.Editor.LumberCampM18PlayModeSmokeTest.RunFromCommandLine' },
    @{ name='m17'; args='-executeMethod IndustryTycoon.Editor.LumberCampM17PlayModeSmokeTest.RunFromCommandLine' },
    @{ name='gameplay'; args='-executeMethod IndustryTycoon.Editor.LumberCampPlayModeSmokeTest.RunFromCommandLine' },
    @{ name='scene-audit'; args='-nographics -executeMethod IndustryTycoon.Editor.LumberCampPrototypeBuilder.ValidateFromCommandLine -quit' },
    @{ name='android-release'; args='-executeMethod IndustryTycoon.Editor.LumberCampPrototypeBuilder.BuildAndroidPlayerFromCommandLine -customBuildPath "'+$root+'/Builds/Android/IndustryTycoon-HudComposition-Release.apk" -quit' }
)
$started = $false
foreach ($job in $jobs) {
    if ($job.name -eq $StartAt) { $started = $true }
    if (-not $started) { continue }
    Write-Output ('START '+$job.name)
    $arguments = '-batchmode -projectPath "'+$root+'" '+$job.args+' -logFile "'+$root+'/Logs/composition-'+$job.name+'.log"'
    $process = Start-Process -FilePath $unity -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
    Write-Output ($job.name+' exit='+$process.ExitCode)
    if ($process.ExitCode -ne 0) { throw ('Validation failed: '+$job.name) }
}
