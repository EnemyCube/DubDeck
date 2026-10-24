param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/media-fixtures'),
    [switch]$IncludeSoak,
    [string]$FFmpeg = 'ffmpeg'
)

$ErrorActionPreference = 'Stop'
$destination = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$encoder = (Get-Command $FFmpeg -ErrorAction Stop).Source

function Invoke-Encode([string]$Name, [string[]]$Options) {
    $path = Join-Path $destination $Name
    if (Test-Path -LiteralPath $path) { Write-Host "Keeping $Name"; return }
    Write-Host "Generating $Name"
    & $encoder -hide_banner -loglevel error -nostdin -y @Options $path
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed for $Name ($LASTEXITCODE). Delete the incomplete file and retry." }
}

# Synthetic test material only. ffmpeg is a developer tool, not an app dependency.
$video = @('-f','lavfi','-i','testsrc2=size=160x90:rate=25','-f','lavfi','-i','sine=frequency=440:sample_rate=48000','-t','4','-c:v','libx264','-preset','ultrafast','-pix_fmt','yuv420p','-c:a','aac','-b:a','64k','-shortest')
Invoke-Encode 'video.mp4' ($video + @('-movflags','+faststart'))
Invoke-Encode 'video.mov' $video
Invoke-Encode 'video.mkv' $video

$audio = @('-f','lavfi','-i','sine=frequency=660:sample_rate=48000','-t','8')
Invoke-Encode 'audio.mp3' ($audio + @('-c:a','libmp3lame','-b:a','96k'))
Invoke-Encode 'audio.wav' ($audio + @('-c:a','pcm_s16le'))
Invoke-Encode 'audio.m4a' ($audio + @('-c:a','aac','-b:a','64k'))
Invoke-Encode 'audio.flac' ($audio + @('-c:a','flac'))
[IO.File]::WriteAllText((Join-Path $destination 'corrupt.mp4'), 'This is deliberately invalid media, not an MP4 container.')

if ($IncludeSoak) {
    # Matching audio/video pulses every second aid an optional manual A/V check.
    # A 605-second source allows ten measured minutes after player warm-up.
    $pulse = @('-f','lavfi','-i',"color=c=black:size=160x90:rate=25,drawbox=x=0:y=0:w=iw:h=ih:color=white:t=fill:enable='lt(mod(t,1),0.08)'",'-f','lavfi','-i',"aevalsrc=if(lt(mod(t\,1)\,0.08)\,0.18*sin(2*PI*880*t)\,0):s=48000",'-t','605','-c:v','libx264','-preset','ultrafast','-pix_fmt','yuv420p','-c:a','aac','-b:a','64k','-movflags','+faststart','-shortest')
    Invoke-Encode 'pulse-10min.mp4' $pulse
    Invoke-Encode 'pulse-10min.flac' @('-i',(Join-Path $destination 'pulse-10min.mp4'),'-vn','-c:a','flac')
}


# Subtitle regression material: black picture with embedded and sidecar SRT cues.
$subtitleText = "1`r`n00:00:00,000 --> 00:00:07,900`r`nVIDEO SYNC SUBTITLE TEST`r`n"
[IO.File]::WriteAllText((Join-Path $destination 'subtitle-sidecar.srt'), $subtitleText, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $destination 'subtitle-embedded.en.srt'), $subtitleText, [Text.UTF8Encoding]::new($false))
Invoke-Encode 'subtitle-sidecar.mp4' @('-f','lavfi','-i','color=c=black:size=640x360:rate=25','-f','lavfi','-i','sine=frequency=440:sample_rate=48000','-t','8','-c:v','libx264','-preset','ultrafast','-pix_fmt','yuv420p','-c:a','aac','-movflags','+faststart')
Invoke-Encode 'subtitle-embedded.mkv' @('-i',(Join-Path $destination 'subtitle-sidecar.mp4'),'-i',(Join-Path $destination 'subtitle-embedded.en.srt'),'-map','0:v','-map','0:a','-map','1:0','-c','copy','-c:s','srt','-metadata:s:s:0','language=eng','-metadata:s:s:0','title=English test captions')

Write-Host "Fixtures ready: $destination"

# Audible voice-like tone for two seconds, silence for two seconds, repeating.
Invoke-Encode 'voice-gated.wav' @('-f','lavfi','-i','aevalsrc=if(lt(mod(t\,4)\,2)\,0.25*sin(2*PI*440*t)\,0):s=16000','-t','12','-c:a','pcm_s16le')