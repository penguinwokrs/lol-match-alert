# Raw LCU websocket dumper: subscribe to the given events and print every frame with a timestamp.
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\lcu-ws.ps1 OnJsonApiEvent_lol-gameflow_v1_gameflow-phase OnJsonApiEvent_lol-matchmaking_v1_ready-check
# Pass OnJsonApiEvent alone to get every event (firehose). Ctrl+C to stop.
$ErrorActionPreference = 'Stop'
$proc = Get-Process LeagueClientUx -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { 'League client is not running'; exit 1 }
$lf = (Get-Content (Join-Path (Split-Path $proc.Path) 'lockfile') -Raw).Split(':')
$auth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("riot:$($lf[3])"))

# LCU uses a self-signed cert; callback must be a real delegate (scriptblocks fail off-thread)
if (-not ('TrustAll' -as [type])) {
    Add-Type -TypeDefinition 'public static class TrustAll { public static bool Ok(object s, System.Security.Cryptography.X509Certificates.X509Certificate c, System.Security.Cryptography.X509Certificates.X509Chain ch, System.Net.Security.SslPolicyErrors e) { return true; } }'
}
$trust = [Delegate]::CreateDelegate([Net.Security.RemoteCertificateValidationCallback], [TrustAll].GetMethod('Ok'))
[Net.ServicePointManager]::ServerCertificateValidationCallback = $trust
$ws = New-Object Net.WebSockets.ClientWebSocket
$ws.Options.SetRequestHeader('Authorization', "Basic $auth")
if ($PSVersionTable.PSVersion.Major -ge 7) { $ws.Options.RemoteCertificateValidationCallback = $trust }
$ws.ConnectAsync([Uri]"wss://127.0.0.1:$($lf[2])/", [Threading.CancellationToken]::None).Wait()
foreach ($ev in $args) {
    $msg = [Text.Encoding]::UTF8.GetBytes("[5, `"$ev`"]")
    $ws.SendAsync([ArraySegment[byte]]$msg, 'Text', $true, [Threading.CancellationToken]::None).Wait()
}
"subscribed: $($args -join ', ')"
$buf = New-Object byte[] 65536
while ($ws.State -eq 'Open') {
    $sb = New-Object Text.StringBuilder
    do {
        $r = $ws.ReceiveAsync([ArraySegment[byte]]$buf, [Threading.CancellationToken]::None).Result
        $sb.Append([Text.Encoding]::UTF8.GetString($buf, 0, $r.Count)) | Out-Null
    } while (-not $r.EndOfMessage)
    $s = $sb.ToString(); if ($s) { "$(Get-Date -Format HH:mm:ss.fff) $s" }
}
