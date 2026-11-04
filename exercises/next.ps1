<#
.SYNOPSIS
  Тренажёр EasyLangApi: показывает первый незакрытый шаг, его условие и что почитать.

.EXAMPLE
  .\exercises\next.ps1              # прогнать тесты тренажёра и показать текущий шаг
  .\exercises\next.ps1 -Hint 1      # то же и подсказку 1 (есть 1, 2, 3)
  .\exercises\next.ps1 -Step 7      # показать шаг 7 без прогона тестов
  .\exercises\next.ps1 -Step 7 -Hint 2
  .\exercises\next.ps1 -List        # состояние всех шагов

  Для CI: -Expect AllRed (на ветке exercises каждый шаг красный)
          -Expect AllGreen (на ветке exercises-solutions все шаги зелёные)

  Работает в Windows PowerShell 5.1 и в PowerShell 7.
#>
param(
    [ValidateRange(0, 3)][int]$Hint = 0,
    [ValidateRange(0, 99)][int]$Step = 0,
    [switch]$List,
    [ValidateSet('', 'AllRed', 'AllGreen')][string]$Expect = ''
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$root = $PSScriptRoot
$stepsDir = Join-Path $root 'steps'
$testsDir = Join-Path $root 'Exercises.Tests'

function Get-Steps {
    Get-ChildItem -Path $stepsDir -Filter '*.md' | Sort-Object Name | ForEach-Object {
        $lines = Get-Content -Path $_.FullName -Encoding UTF8
        [pscustomobject]@{
            Id    = $_.Name.Substring(0, 2)
            Title = ($lines[0] -replace '^#\s*', '')
            Lines = $lines
        }
    }
}

function Get-Section([string[]]$lines, [string]$heading) {
    $out = @()
    $inside = $false
    foreach ($line in $lines) {
        if ($line -match '^## ') {
            if ($inside) { break }
            if ($line -eq "## $heading") { $inside = $true; continue }
        }
        if ($inside) { $out += $line }
    }
    return $out
}

function Show-Step($stepInfo, [int]$hintLevel) {
    Write-Host ''
    $shown = @()
    foreach ($line in $stepInfo.Lines) {
        if ($line -match '^## Подсказка') { break }
        $shown += $line
    }
    $shown | ForEach-Object { Write-Host $_ }
    if ($hintLevel -gt 0) {
        Write-Host "## Подсказка $hintLevel" -ForegroundColor Yellow
        Get-Section $stepInfo.Lines "Подсказка $hintLevel" | ForEach-Object { Write-Host $_ }
    } else {
        Write-Host 'Застряли? Подсказки: -Hint 1, -Hint 2, -Hint 3 (готового решения в них нет).' -ForegroundColor DarkGray
    }
}

function Invoke-ExerciseTests {
    $resultsDir = Join-Path ([System.IO.Path]::GetTempPath()) ('easylang-exercises-' + [guid]::NewGuid().ToString('N'))
    Write-Host 'Прогоняю тесты тренажёра (первый раз — около минуты)...' -ForegroundColor DarkGray
    $output = & dotnet test $testsDir --nologo --logger 'trx;LogFileName=exercises.trx' --results-directory $resultsDir 2>&1
    $trx = Join-Path $resultsDir 'exercises.trx'
    if (-not (Test-Path $trx)) {
        Write-Host 'Тесты не запустились: скорее всего, код не собирается. Ошибки сборки:' -ForegroundColor Red
        $output | Where-Object { $_ -match 'error' } | Select-Object -First 15 | ForEach-Object { Write-Host "  $_" }
        exit 2
    }

    [xml]$xml = Get-Content -Path $trx -Raw -Encoding UTF8
    $results = @()
    foreach ($r in $xml.TestRun.Results.UnitTestResult) {
        $message = ''
        if ($r.Output -and $r.Output.ErrorInfo -and $r.Output.ErrorInfo.Message) {
            $message = ($r.Output.ErrorInfo.Message -split "`n")[0].Trim()
        }
        $results += [pscustomobject]@{ Name = $r.testName; Outcome = $r.outcome; Message = $message }
    }
    Remove-Item -Recurse -Force $resultsDir -ErrorAction SilentlyContinue
    return $results
}

function Get-StepStatus($steps, $results) {
    foreach ($s in $steps) {
        $mine = @($results | Where-Object { $_.Name -like "Exercises.Step$($s.Id).*" })
        $failed = @($mine | Where-Object { $_.Outcome -ne 'Passed' })
        [pscustomobject]@{
            Step   = $s
            Total  = $mine.Count
            Failed = $failed
            Done   = ($mine.Count -gt 0 -and $failed.Count -eq 0)
        }
    }
}

$steps = @(Get-Steps)

# Показ шага по номеру — без прогона тестов.
if ($Step -gt 0 -and $Expect -eq '' -and -not $List) {
    $id = '{0:D2}' -f $Step
    $chosen = $steps | Where-Object { $_.Id -eq $id }
    if (-not $chosen) { Write-Host "Шага $id нет. Шаги: 01–$($steps[-1].Id)." -ForegroundColor Red; exit 1 }
    Show-Step $chosen $Hint
    exit 0
}

$results = Invoke-ExerciseTests
$status = @(Get-StepStatus $steps $results)
$done = @($status | Where-Object { $_.Done }).Count

if ($Expect -ne '') {
    $bad = @()
    foreach ($s in $status) {
        if ($s.Total -eq 0) { $bad += "шаг $($s.Step.Id): нет ни одного теста" }
        elseif ($Expect -eq 'AllRed' -and $s.Done) { $bad += "шаг $($s.Step.Id): тесты зелёные, хотя шаг не сделан" }
        elseif ($Expect -eq 'AllGreen' -and -not $s.Done) { $bad += "шаг $($s.Step.Id): красных тестов $($s.Failed.Count)" }
    }
    Write-Host "Шагов: $($status.Count), закрыто: $done, ожидание: $Expect"
    if ($bad.Count -gt 0) { $bad | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }; exit 1 }
    Write-Host 'Ожидание выполнено.' -ForegroundColor Green
    exit 0
}

if ($List) {
    foreach ($s in $status) {
        $mark = '[ ]'
        if ($s.Done) { $mark = '[x]' }
        Write-Host ("{0} {1}" -f $mark, $s.Step.Title)
    }
    Write-Host ''
    Write-Host "Закрыто шагов: $done из $($status.Count)."
    exit 0
}

Write-Host "Закрыто шагов: $done из $($status.Count)." -ForegroundColor Cyan
$current = $status | Where-Object { -not $_.Done } | Select-Object -First 1
if (-not $current) {
    Write-Host 'Все шаги закрыты. Осталось закоммитить и объяснить каждую строку вслух.' -ForegroundColor Green
    exit 0
}

Show-Step $current.Step $Hint
Write-Host ''
Write-Host 'Красные тесты этого шага:' -ForegroundColor Red
foreach ($f in $current.Failed) {
    Write-Host "  $($f.Name)"
    if ($f.Message) { Write-Host "    $($f.Message)" -ForegroundColor DarkGray }
}
Write-Host ''
Write-Host 'Сделали шаг — запустите next.ps1 снова и закоммитьте.' -ForegroundColor DarkGray
