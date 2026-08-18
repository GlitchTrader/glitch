$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$ninjaRoot = 'C:\Program Files\NinjaTrader 8\bin'
$resolveDependency = [System.ResolveEventHandler] {
    param($sender, $eventArgs)
    $simpleName = ([System.Reflection.AssemblyName]::new($eventArgs.Name)).Name + '.dll'
    $candidate = Join-Path $ninjaRoot $simpleName
    if (Test-Path -LiteralPath $candidate) { return [System.Reflection.Assembly]::LoadFrom($candidate) }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolveDependency)
foreach ($dependency in @(
    'System.Runtime.CompilerServices.Unsafe.dll', 'System.Buffers.dll', 'System.Memory.dll',
    'System.Collections.Immutable.dll', 'System.Reflection.Metadata.dll', 'Microsoft.Bcl.Memory.dll',
    'Microsoft.CodeAnalysis.dll', 'Microsoft.CodeAnalysis.CSharp.dll')) {
    [void][System.Reflection.Assembly]::LoadFrom((Join-Path $ninjaRoot $dependency))
}

$sourcePaths = @(
    (Join-Path $repoRoot 'ninjatrader\Glitch\AddOns\GlitchAddOn\Services\Ai\GlitchAiDailyCaptureProtectionPlanner.cs'),
    (Join-Path $repoRoot 'tools\tests\GlitchAiDailyCaptureProtectionHarness.cs')
)
$syntaxTrees = [System.Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
foreach ($sourcePath in $sourcePaths) {
    $syntaxTrees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText(
        [System.IO.File]::ReadAllText($sourcePath), $null, $sourcePath,
        [System.Text.Encoding]::UTF8))
}
$referencePaths = @(
    [object].Assembly.Location,
    [System.Console].Assembly.Location,
    [System.Linq.Enumerable].Assembly.Location
) | Select-Object -Unique
$references = [System.Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
foreach ($referencePath in $referencePaths) {
    $references.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($referencePath))
}
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create(
    'GlitchAiDailyCaptureProtectionHarness', $syntaxTrees, $references,
    [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new(
        [Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication))
$outputPath = Join-Path ([System.IO.Path]::GetTempPath()) (
    'GlitchAiDailyCaptureProtectionHarness-' + [Guid]::NewGuid().ToString('N') + '.exe')
$stream = [System.IO.File]::Open($outputPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
try { $emitResult = $compilation.Emit($stream) } finally { $stream.Dispose() }
if (-not $emitResult.Success) {
    Remove-Item -LiteralPath $outputPath -Force -ErrorAction SilentlyContinue
    throw (($emitResult.Diagnostics |
        Where-Object { $_.Severity -eq [Microsoft.CodeAnalysis.DiagnosticSeverity]::Error } |
        ForEach-Object { $_.ToString() }) -join [Environment]::NewLine)
}
try {
    & $outputPath
    if ($LASTEXITCODE -ne 0) { throw "AI daily capture protection harness exited with code $LASTEXITCODE" }
}
finally {
    Remove-Item -LiteralPath $outputPath -Force -ErrorAction SilentlyContinue
}
