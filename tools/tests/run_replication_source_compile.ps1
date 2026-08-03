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
    (Join-Path $repoRoot 'ninjatrader\Glitch\AddOns\GlitchAddOn\Services\Trading\GlitchInstrumentMetadataService.cs'),
    (Join-Path $repoRoot 'ninjatrader\Glitch\AddOns\GlitchAddOn\Services\Trading\GlitchReplicationMath.cs'),
    (Join-Path $repoRoot 'ninjatrader\Glitch\AddOns\GlitchAddOn\Services\Trading\GlitchReplicationEngine.cs'),
    (Join-Path $repoRoot 'ninjatrader\Glitch\AddOns\GlitchAddOn\Services\Trading\GlitchReplicationProtection.cs'),
    (Join-Path $repoRoot 'ninjatrader\Glitch\AddOns\GlitchAddOn\Services\Trading\GlitchCopyEngine.cs')
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

$syntaxOnlyPaths = @(
    (Join-Path $repoRoot 'ninjatrader\Glitch\AddOns\GlitchAddOn\UI\MainWindow\GlitchMainWindow.Replication.partial.cs')
)
foreach ($sourcePath in $syntaxOnlyPaths) {
    $syntaxTree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText(
        [System.IO.File]::ReadAllText($sourcePath),
        $null,
        $sourcePath,
        [System.Text.Encoding]::UTF8
    )
    $syntaxErrors = $syntaxTree.GetDiagnostics() |
        Where-Object { $_.Severity -eq [Microsoft.CodeAnalysis.DiagnosticSeverity]::Error } |
        ForEach-Object { $_.ToString() }
    if ($syntaxErrors) {
        throw ($syntaxErrors -join [Environment]::NewLine)
    }
}

$referencePaths = @(
    [object].Assembly.Location,
    [System.Linq.Enumerable].Assembly.Location,
    [System.Uri].Assembly.Location,
    [System.Threading.Tasks.Task].Assembly.Location,
    (Join-Path $ninjaRoot 'NinjaTrader.Core.dll')
) | Select-Object -Unique
$references = [System.Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
foreach ($referencePath in $referencePaths) {
    $references.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($referencePath))
}

$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new(
    [Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary
)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create(
    'GlitchReplicationSourceCompile',
    $syntaxTrees,
    $references,
    $options
)
$outputPath = Join-Path ([System.IO.Path]::GetTempPath()) 'GlitchReplicationSourceCompile.dll'
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

Write-Output 'replication source compile: PASS'
