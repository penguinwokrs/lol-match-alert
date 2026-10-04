# lol-match-alert: toast + sound when League of Legends finds a match.
# Works on Windows PowerShell 5.1 (preinstalled) and pwsh 7.
#   powershell -NoProfile -ExecutionPolicy Bypass -File lol-match-alert.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File lol-match-alert.ps1 -Test   # fire the alert once
param(
    [string]$Sound = "$env:windir\Media\Alarm01.wav",
    [switch]$Test
)
$ErrorActionPreference = 'Stop'
$Event = 'OnJsonApiEvent_lol-gameflow_v1_gameflow-phase'

function Write-Log($msg) { "$(Get-Date -Format 'HH:mm:ss') $msg" }

# WinRT toast API. Only Windows PowerShell 5.1 can load WinRT types, so pwsh 7 hands this to powershell.exe.
$ToastScript = @'
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null
$xml = New-Object Windows.Data.Xml.Dom.XmlDocument
$xml.LoadXml('<toast scenario="alarm"><visual><binding template="ToastGeneric"><text>Match found</text><text>Accept the ready check</text></binding></visual><audio silent="true"/></toast>')
$appId = '{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe'
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($appId).Show((New-Object Windows.UI.Notifications.ToastNotification $xml))
'@

function Show-Alert {
    # Toast first (async), then the sound. Toast audio is muted under Focus Assist / DND, SoundPlayer is not.
    try {
        if ($PSVersionTable.PSVersion.Major -ge 7) { powershell.exe -NoProfile -NonInteractive -Command $ToastScript }
        else { Invoke-Expression $ToastScript }
    } catch { Write-Log "toast failed: $_" }
    try { (New-Object System.Media.SoundPlayer $Sound).PlaySync() } catch { Write-Log "sound failed: $_" }
}

function Handle-Frame([string]$frame) {
    # WAMP event: [8, "<event>", {"data": "ReadyCheck", "eventType": "Update", "uri": "..."}]
    $msg = $frame | ConvertFrom-Json
    if ($msg[0] -eq 8 -and $msg[2].data -eq 'ReadyCheck') { Write-Log 'match found'; Show-Alert }
}

if ($Test) {
    Handle-Frame "[8,`"$Event`",{`"data`":`"ReadyCheck`",`"eventType`":`"Update`",`"uri`":`"/lol-gameflow/v1/gameflow-phase`"}]"
    exit
}

# LCU uses a self-signed cert. The callback must be a real delegate: scriptblocks die off-thread.
if (-not ('TrustAll' -as [type])) {
    Add-Type -TypeDefinition 'public static class TrustAll { public static bool Ok(object s, System.Security.Cryptography.X509Certificates.X509Certificate c, System.Security.Cryptography.X509Certificates.X509Chain ch, System.Net.Security.SslPolicyErrors e) { return true; } }'
}
$trust = [Delegate]::CreateDelegate([Net.Security.RemoteCertificateValidationCallback], [TrustAll].GetMethod('Ok'))
[Net.ServicePointManager]::ServerCertificateValidationCallback = $trust   # .NET Framework path (PS 5.1)

$buf = New-Object byte[] 65536
while ($true) {
    $proc = Get-Process LeagueClientUx -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $proc) { Start-Sleep 5; continue }
    try {
        # lockfile: name:pid:port:password:protocol, rotates on every client launch
        $lf = (Get-Content (Join-Path (Split-Path $proc.Path) 'lockfile') -Raw).Split(':')
        $auth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("riot:$($lf[3])"))
        $ws = New-Object Net.WebSockets.ClientWebSocket
        $ws.Options.SetRequestHeader('Authorization', "Basic $auth")
        if ($PSVersionTable.PSVersion.Major -ge 7) { $ws.Options.RemoteCertificateValidationCallback = $trust }   # .NET Core ignores ServicePointManager
        $ws.ConnectAsync([Uri]"wss://127.0.0.1:$($lf[2])/", [Threading.CancellationToken]::None).Wait()
        $sub = [Text.Encoding]::UTF8.GetBytes("[5, `"$Event`"]")
        $ws.SendAsync([ArraySegment[byte]]$sub, 'Text', $true, [Threading.CancellationToken]::None).Wait()
        Write-Log "connected to LCU on port $($lf[2]), waiting for a match"
        while ($ws.State -eq 'Open') {
            $sb = New-Object Text.StringBuilder
            do {
                $r = $ws.ReceiveAsync([ArraySegment[byte]]$buf, [Threading.CancellationToken]::None).Result
                $sb.Append([Text.Encoding]::UTF8.GetString($buf, 0, $r.Count)) | Out-Null
            } while (-not $r.EndOfMessage)
            if ($sb.Length) { Handle-Frame $sb.ToString() }
        }
        Write-Log 'socket closed'
    } catch {
        $e = $_.Exception; while ($e.InnerException) { $e = $e.InnerException }
        Write-Log "disconnected: $($e.Message)"
    }
    Start-Sleep 5   # client restarting or lockfile up before the socket: just retry
}
