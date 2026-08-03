$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$roslynRoot = 'C:\Program Files\NinjaTrader 8\bin'
$codeAnalysis = Join-Path $roslynRoot 'Microsoft.CodeAnalysis.dll'
$csharpAnalysis = Join-Path $roslynRoot 'Microsoft.CodeAnalysis.CSharp.dll'
if (-not (Test-Path -LiteralPath $codeAnalysis) -or -not (Test-Path -LiteralPath $csharpAnalysis)) {
    throw 'NinjaTrader Roslyn compiler assemblies were not found.'
}

$resolveDependency = [System.ResolveEventHandler] {
    param($sender, $eventArgs)
    $simpleName = ([System.Reflection.AssemblyName]::new($eventArgs.Name)).Name + '.dll'
    $candidate = Join-Path $roslynRoot $simpleName
    if (Test-Path -LiteralPath $candidate) {
        return [System.Reflection.Assembly]::LoadFrom($candidate)
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolveDependency)

foreach ($dependency in @(
    'System.Runtime.CompilerServices.Unsafe.dll',
    'System.Buffers.dll',
    'System.Memory.dll',
    'System.Collections.Immutable.dll',
    'System.Reflection.Metadata.dll',
    'Microsoft.Bcl.Memory.dll'
)) {
    [void][System.Reflection.Assembly]::LoadFrom((Join-Path $roslynRoot $dependency))
}
Add-Type -Path $codeAnalysis
Add-Type -Path $csharpAnalysis

$sourcePaths = @(
    (Join-Path $repoRoot 'ninjatrader\Glitch\AddOns\GlitchAddOn\Services\Insights\GlitchTradeInsightsService.cs'),
    (Join-Path $repoRoot 'ninjatrader\Glitch\AddOns\GlitchAddOn\Services\Insights\GlitchTradeLedgerService.cs'),
    (Join-Path $repoRoot 'tools\tests\GlitchTradeLedgerPartialFillHarness.cs')
)
$syntaxTrees = [System.Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
foreach ($sourcePath in $sourcePaths) {
    $sourceText = [System.IO.File]::ReadAllText($sourcePath)
    $syntaxTrees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($sourceText, $null, $sourcePath, [System.Text.Encoding]::UTF8))
}

foreach ($syntaxOnlyPath in @(
    (Join-Path $repoRoot 'ninjatrader\Glitch\AddOns\GlitchAddOn\UI\MainWindow\GlitchMainWindow.SummaryTab.partial.cs'),
    (Join-Path $repoRoot 'ninjatrader\Glitch\AddOns\GlitchAddOn\UI\MainWindow\GlitchMainWindow.Performance.partial.cs'),
    (Join-Path $repoRoot 'ninjatrader\Glitch\AddOns\GlitchAddOn\Tests\GlitchTradeInsightsServiceTests.cs')
)) {
    $syntaxOnlyTree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText(
        [System.IO.File]::ReadAllText($syntaxOnlyPath),
        $null,
        $syntaxOnlyPath,
        [System.Text.Encoding]::UTF8
    )
    $syntaxErrors = $syntaxOnlyTree.GetDiagnostics() |
        Where-Object { $_.Severity -eq [Microsoft.CodeAnalysis.DiagnosticSeverity]::Error }
    if ($syntaxErrors) {
        throw (($syntaxErrors | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine)
    }
}

$referencePaths = @(
    [object].Assembly.Location,
    [System.Linq.Enumerable].Assembly.Location,
    [System.Uri].Assembly.Location,
    [System.Threading.Tasks.Task].Assembly.Location
) | Select-Object -Unique
$references = [System.Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
foreach ($referencePath in $referencePaths) {
    $references.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($referencePath))
}

$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new(
    [Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication,
    $null,
    'GlitchTradeLedgerPartialFillHarness'
)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create(
    'GlitchTradeLedgerPartialFillHarness',
    $syntaxTrees,
    $references,
    $options
)

$outputPath = Join-Path ([System.IO.Path]::GetTempPath()) 'GlitchTradeLedgerPartialFillHarness.exe'
$stream = [System.IO.File]::Open($outputPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
try {
    $emitResult = $compilation.Emit($stream)
}
finally {
    $stream.Dispose()
}

if (-not $emitResult.Success) {
    $diagnostics = $emitResult.Diagnostics |
        Where-Object { $_.Severity -eq [Microsoft.CodeAnalysis.DiagnosticSeverity]::Error } |
        ForEach-Object { $_.ToString() }
    throw ($diagnostics -join [Environment]::NewLine)
}

& $outputPath
if ($LASTEXITCODE -ne 0) {
    throw "TradeLedger partial-fill harness failed with exit code $LASTEXITCODE."
}

