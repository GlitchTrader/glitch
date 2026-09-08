param([string]$SourceRevision)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$ninjaRoot = 'C:\Program Files\NinjaTrader 8\bin'
$resolveDependency = [System.ResolveEventHandler] {
    param($sender, $eventArgs)
    $candidate = Join-Path $ninjaRoot (([Reflection.AssemblyName]::new($eventArgs.Name)).Name + '.dll')
    if (Test-Path -LiteralPath $candidate) { return [Reflection.Assembly]::LoadFrom($candidate) }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolveDependency)
foreach ($dependency in @(
    'System.Runtime.CompilerServices.Unsafe.dll', 'System.Buffers.dll', 'System.Memory.dll',
    'System.Collections.Immutable.dll', 'System.Reflection.Metadata.dll', 'Microsoft.Bcl.Memory.dll',
    'Microsoft.CodeAnalysis.dll', 'Microsoft.CodeAnalysis.CSharp.dll')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $ninjaRoot $dependency))
}
$sourcePaths = @(
    'ninjatrader/Glitch/AddOns/GlitchAddOn/Core/GlitchContracts.cs',
    'ninjatrader/Glitch/AddOns/GlitchAddOn/Core/GlitchEngine.cs',
    'ninjatrader/Glitch/AddOns/GlitchAddOn/Core/GlitchNativeIdentity.cs',
    'ninjatrader/Glitch/AddOns/GlitchAddOn/Infrastructure/NinjaTraderGateway.cs',
    'tools/tests/GlitchNativeSafetyDoubles.cs',
    'tools/tests/GlitchNativeSafetyHarness.cs'
)
$syntaxTrees = [Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
$parseOptions = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default
if ($SourceRevision) { $parseOptions = $parseOptions.WithPreprocessorSymbols([string[]]@('BASELINE')) }
foreach ($relativePath in $sourcePaths) {
    $sourcePath = Join-Path $repoRoot $relativePath
    if ($SourceRevision -and $relativePath.StartsWith('ninjatrader/')) {
        $sourceText = (& git -C $repoRoot show "${SourceRevision}:$relativePath") -join "`n"
        if ($LASTEXITCODE -ne 0) { throw "Cannot read source revision $SourceRevision" }
    } else { $sourceText = [IO.File]::ReadAllText($sourcePath) }
    $syntaxTrees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText(
        $sourceText, $parseOptions, $sourcePath, [Text.Encoding]::UTF8))
}
$references = [Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
foreach ($path in @([object].Assembly.Location, [Console].Assembly.Location,
    [Uri].Assembly.Location, [Linq.Enumerable].Assembly.Location) | Select-Object -Unique) {
    $references.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($path))
}
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create(
    'GlitchNativeSafetyHarness', $syntaxTrees, $references,
    [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new(
        [Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication))
$outputPath = Join-Path ([IO.Path]::GetTempPath()) ('GlitchNativeSafety-' + [guid]::NewGuid().ToString('N') + '.exe')
$stream = [IO.File]::Open($outputPath, [IO.FileMode]::Create, [IO.FileAccess]::Write)
try { $result = $compilation.Emit($stream) } finally { $stream.Dispose() }
try {
    if (-not $result.Success) {
        throw (($result.Diagnostics | Where-Object { $_.Severity -eq 'Error' } |
            ForEach-Object { $_.ToString() }) -join [Environment]::NewLine)
    }
    & $outputPath
    if ($LASTEXITCODE -ne 0) { throw "native safety harness exited with code $LASTEXITCODE" }
} finally { Remove-Item -LiteralPath $outputPath -Force }
