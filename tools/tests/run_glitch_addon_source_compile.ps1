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
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName System.Xaml
Add-Type -AssemblyName System.Web.Extensions
Add-Type -AssemblyName System.Xml.Linq

$sourcePaths = Get-ChildItem -Path (Join-Path $repoRoot 'ninjatrader\Glitch\AddOns\GlitchAddOn') -Recurse -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '\\Tests\\' } |
    Select-Object -ExpandProperty FullName
$sourcePaths += @(
    (Join-Path $repoRoot 'ninjatrader\Glitch\Indicators\glitch\GlitchBridgeBusCompat.cs'),
    (Join-Path $repoRoot 'ninjatrader\Glitch\Indicators\glitch\GlitchMarketSnapshotJson.cs'),
    (Join-Path $repoRoot 'ninjatrader\Glitch\Indicators\glitch\GlitchMarketSnapshotRawJson.cs')
)
$syntaxTrees = [System.Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
foreach ($sourcePath in $sourcePaths) {
    $syntaxTrees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText(
        [System.IO.File]::ReadAllText($sourcePath), $null, $sourcePath, [System.Text.Encoding]::UTF8))
}

$referencePaths = @(
    [object].Assembly.Location,
    [System.Console].Assembly.Location,
    [System.Uri].Assembly.Location,
    [System.Linq.Enumerable].Assembly.Location,
    [System.Net.Http.HttpClient].Assembly.Location,
    [System.Web.Script.Serialization.JavaScriptSerializer].Assembly.Location,
    [System.Xml.Linq.XDocument].Assembly.Location,
    [System.Xml.XmlReader].Assembly.Location,
    [System.Windows.Window].Assembly.Location,
    [System.Windows.Media.Brush].Assembly.Location,
    [System.Windows.DependencyObject].Assembly.Location,
    [System.Xaml.XamlReader].Assembly.Location,
    (Join-Path $ninjaRoot 'NinjaTrader.Core.dll'),
    (Join-Path $ninjaRoot 'NinjaTrader.Gui.dll'),
    (Join-Path $ninjaRoot 'Microsoft.Web.WebView2.Core.dll'),
    (Join-Path $ninjaRoot 'Microsoft.Web.WebView2.Wpf.dll')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -Unique
$references = [System.Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
foreach ($referencePath in $referencePaths) {
    $references.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($referencePath))
}
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create(
    'GlitchAddOnSourceCompile', $syntaxTrees, $references,
    [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary))
$outputPath = Join-Path ([System.IO.Path]::GetTempPath()) (
    'GlitchAddOnSourceCompile-' + [Guid]::NewGuid().ToString('N') + '.dll')
$stream = [System.IO.File]::Open($outputPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
try { $emitResult = $compilation.Emit($stream) } finally { $stream.Dispose() }
Remove-Item -LiteralPath $outputPath -Force
if (-not $emitResult.Success) {
    throw (($emitResult.Diagnostics | Where-Object { $_.Severity -eq [Microsoft.CodeAnalysis.DiagnosticSeverity]::Error } |
        ForEach-Object { $_.ToString() }) -join [Environment]::NewLine)
}
Write-Output "Glitch AddOn source compile: PASS ($($sourcePaths.Count) files)"
