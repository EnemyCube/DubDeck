# Render the repository's vector app icon to PNG and a multi-resolution Windows ICO.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$assetFolder = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/DubDeck.App/Assets'))
[xml]$svg = [IO.File]::ReadAllText((Join-Path $assetFolder 'DubDeck.svg'))
$brushConverter = New-Object Windows.Media.BrushConverter
function Svg-Brush([string]$value) {
    if ([string]::IsNullOrWhiteSpace($value) -or $value -eq 'none') { return $null }
    return $brushConverter.ConvertFromInvariantString($value)
}
$group = New-Object Windows.Media.DrawingGroup
$context = $group.Open()
try {
    foreach ($node in $svg.DocumentElement.ChildNodes) {
        if ($node.LocalName -in @('title','desc')) { continue }
        $fill = Svg-Brush $node.GetAttribute('fill')
        $pen = $null
        if ($node.HasAttribute('stroke')) {
            $pen = New-Object Windows.Media.Pen ((Svg-Brush $node.GetAttribute('stroke')), [double]$node.GetAttribute('stroke-width'))
            $pen.StartLineCap = $pen.EndLineCap = [Windows.Media.PenLineCap]::Round
            $pen.LineJoin = [Windows.Media.PenLineJoin]::Round
        }
        if ($node.LocalName -eq 'rect') {
            $rect = New-Object Windows.Rect ([double]$node.GetAttribute('x'), [double]$node.GetAttribute('y'), [double]$node.GetAttribute('width'), [double]$node.GetAttribute('height'))
            $context.DrawRoundedRectangle($fill,$pen,$rect,[double]$node.GetAttribute('rx'),[double]$node.GetAttribute('rx'))
        } elseif ($node.LocalName -eq 'path') {
            $context.DrawGeometry($fill,$pen,[Windows.Media.Geometry]::Parse($node.GetAttribute('d')))
        } else { throw ('Unsupported app icon element: ' + $node.LocalName) }
    }
} finally { $context.Close() }
$group.Freeze()
$sizes = @(16,24,32,48,64,128,256)
$images = New-Object 'Collections.Generic.List[byte[]]'
foreach ($size in $sizes) {
    $visual = New-Object Windows.Media.DrawingVisual
    $drawing = $visual.RenderOpen()
    $drawing.PushTransform(([Windows.Media.ScaleTransform]::new(($size/256.0),($size/256.0))))
    $drawing.DrawDrawing($group)
    $drawing.Pop()
    $drawing.Close()
    $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap ($size,$size,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object IO.MemoryStream
    try {
        $encoder.Save($stream)
        $bytes = $stream.ToArray()
        $images.Add($bytes)
        if ($size -eq 256) { [IO.File]::WriteAllBytes((Join-Path $assetFolder 'DubDeck.png'),$bytes) }
    } finally { $stream.Dispose() }
}
$iconStream = [IO.File]::Create((Join-Path $assetFolder 'DubDeck.ico'))
$writer = New-Object IO.BinaryWriter $iconStream
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i=0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
} finally { $writer.Dispose() }
Write-Output ('App icon generated: ' + (Join-Path $assetFolder 'DubDeck.ico'))
