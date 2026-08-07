$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$ninjaRoot = 'C:\Program Files\NinjaTrader 8\bin'
$resolveDependency = [System.ResolveEventHandler] {
    param($sender, $eventArgs)
    $simpleName = ([System.Reflection.AssemblyName]::new($eventArgs.Name)).Name + '.dll'
    $candidate = Join-Path $ninjaRoot $simpleName
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
    'Microsoft.Bcl.Memory.dll',
    'Microsoft.CodeAnalysis.dll',
    'Microsoft.CodeAnalysis.CSharp.dll'
)) {
    [void][System.Reflection.Assembly]::LoadFrom((Join-Path $ninjaRoot $dependency))
}

$sourcePaths = @(
    (Join-Path $repoRoot 'ninjatrader\Glitch\AddOns\GlitchAddOn\Core\GlitchContracts.cs'),
    (Join-Path $repoRoot 'ninjatrader\Glitch\AddOns\GlitchAddOn\Core\GlitchRuntime.cs'),
    (Join-Path $repoRoot 'ninjatrader\Glitch\AddOns\GlitchAddOn\Infrastructure\GlitchMutationGate.cs'),
    (Join-Path $repoRoot 'ninjatrader\Glitch\AddOns\GlitchAddOn\Infrastructure\GlitchRuntimeOwnershipLease.cs'),
    (Join-Path $repoRoot 'tools\tests\GlitchRuntimeLifecycleHarness.cs')
)
$syntaxTrees = [System.Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
foreach ($sourcePath in $sourcePaths) {
    $syntaxTrees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText(
        [System.IO.File]::ReadAllText($sourcePath),
        $null,
        $sourcePath,
        [System.Text.Encoding]::UTF8
    ))
}

$referencePaths = @(
    [object].Assembly.Location,
    [System.Console].Assembly.Location,
    [System.Linq.Enumerable].Assembly.Location,
    [System.Collections.Concurrent.BlockingCollection[object]].Assembly.Location,
    [System.Threading.Thread].Assembly.Location
) | Select-Object -Unique
$references = [System.Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
foreach ($referencePath in $referencePaths) {
    $references.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($referencePath))
}

$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new(
    [Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication
)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create(
    'GlitchRuntimeLifecycleHarness',
    $syntaxTrees,
    $references,
    $options
)
$outputPath = Join-Path ([System.IO.Path]::GetTempPath()) 'GlitchRuntimeLifecycleHarness.exe'
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
    throw "runtime lifecycle harness exited with code $LASTEXITCODE"
}
