Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'
$script:ScriptPath = $PSCommandPath
$script:Servers = @(
    [pscustomobject]@{ Name = 'nat64.net · 阿姆斯特丹'; Address = '2a00:1098:2b::1' },
    [pscustomobject]@{ Name = 'nat64.net · 纽伦堡'; Address = '2a01:4f8:c2c:123f::1' },
    [pscustomobject]@{ Name = 'nat64.net · 赫尔辛基'; Address = '2a01:4f9:c010:3f02::1' }
)
$script:StatePath = Join-Path $env:LOCALAPPDATA 'NAT64Switch\state.json'

function Start-Elevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { return }

    $quotedPath = '"' + $script:ScriptPath.Replace('"', '\"') + '"'
    Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" `
        -Verb RunAs -ArgumentList "-NoLogo -NoProfile -ExecutionPolicy Bypass -STA -File $quotedPath"
    exit
}

function Get-EligibleAdapters {
    @(
        Get-NetIPConfiguration | Where-Object {
            $_.NetAdapter.Status -eq 'Up' -and
            @($_.IPv6Address | Where-Object {
                $_.IPAddress -and $_.IPAddress -notlike 'fe80:*' -and $_.IPAddress -ne '::1'
            }).Count -gt 0
        } | Sort-Object InterfaceAlias | ForEach-Object {
            [pscustomobject]@{
                Name = $_.InterfaceAlias
                InterfaceIndex = $_.InterfaceIndex
                InterfaceGuid = [string]$_.NetAdapter.InterfaceGuid
                IPv6 = @($_.IPv6Address | ForEach-Object { $_.IPAddress })
            }
        }
    )
}

function Get-CurrentDns([int]$InterfaceIndex) {
    $result = Get-DnsClientServerAddress -InterfaceIndex $InterfaceIndex -AddressFamily IPv6 -ErrorAction Stop
    @($result.ServerAddresses | Where-Object { $_ })
}

function Read-SavedState {
    if (-not (Test-Path -LiteralPath $script:StatePath)) { return @() }
    try {
        $state = Get-Content -LiteralPath $script:StatePath -Raw | ConvertFrom-Json
        return @($state.Adapters)
    } catch {
        return @()
    }
}

function Write-SavedState($Entries) {
    $directory = Split-Path -Parent $script:StatePath
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    $payload = [pscustomobject]@{ Adapters = @($Entries) } | ConvertTo-Json -Depth 6
    [System.IO.File]::WriteAllText($script:StatePath, $payload, (New-Object System.Text.UTF8Encoding($false)))
}

function Get-SavedAdapterState($Adapter) {
    @(Read-SavedState | Where-Object { $_.InterfaceGuid -eq $Adapter.InterfaceGuid } | Select-Object -First 1)[0]
}

function Test-Nat64([string]$ServerAddress) {
    $prefixRecords = @(
        Resolve-DnsName -Name 'ipv4only.arpa' -Type AAAA -Server $ServerAddress -DnsOnly -ErrorAction Stop |
            Where-Object { $_.Type -eq 'AAAA' -and $_.IPAddress }
    )
    if ($prefixRecords.Count -eq 0) { throw 'DNS64 没有为 ipv4only.arpa 返回合成 AAAA。' }

    $testRecords = @(
        Resolve-DnsName -Name 'ipv4.google.com' -Type AAAA -Server $ServerAddress -DnsOnly -ErrorAction Stop |
            Where-Object { $_.Type -eq 'AAAA' -and $_.IPAddress }
    )
    if ($testRecords.Count -eq 0) { throw '测试域名没有得到 IPv6 地址。' }

    $connected = $false
    $lastError = $null
    foreach ($record in $testRecords) {
        $client = New-Object System.Net.Sockets.TcpClient([System.Net.Sockets.AddressFamily]::InterNetworkV6)
        try {
            $task = $client.BeginConnect([System.Net.IPAddress]::Parse($record.IPAddress), 443, $null, $null)
            if ($task.AsyncWaitHandle.WaitOne(5000)) {
                $client.EndConnect($task)
                if ($client.Connected) { $connected = $true; break }
            } else {
                $lastError = 'TCP 443 连接超时。'
            }
        } catch {
            $lastError = $_.Exception.GetBaseException().Message
        } finally {
            $client.Dispose()
        }
    }
    if (-not $connected) {
        if ($lastError) { throw $lastError }
        throw 'NAT64 地址连接失败。'
    }
    return "DNS64 与 NAT64 连通；已通过合成 IPv6 地址连接 ipv4.google.com:443。"
}

Start-Elevated
[System.Windows.Forms.Application]::EnableVisualStyles()

$form = New-Object System.Windows.Forms.Form
$form.Text = 'NAT64 Switch'
$form.StartPosition = 'CenterScreen'
$form.Size = New-Object System.Drawing.Size(540, 510)
$form.MinimumSize = New-Object System.Drawing.Size(500, 470)
$form.BackColor = [System.Drawing.Color]::FromArgb(246, 248, 251)
$form.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 10)

$title = New-Object System.Windows.Forms.Label
$title.Text = '校园网 IPv6 转换'
$title.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 19, [System.Drawing.FontStyle]::Bold)
$title.ForeColor = [System.Drawing.Color]::FromArgb(27, 42, 65)
$title.Location = New-Object System.Drawing.Point(28, 22)
$title.Size = New-Object System.Drawing.Size(450, 38)
$form.Controls.Add($title)

$subtitle = New-Object System.Windows.Forms.Label
$subtitle.Text = '开启后，支持 DNS 的 IPv4-only 网站可尝试通过 IPv6 NAT64 访问。'
$subtitle.ForeColor = [System.Drawing.Color]::FromArgb(92, 104, 119)
$subtitle.Location = New-Object System.Drawing.Point(30, 64)
$subtitle.Size = New-Object System.Drawing.Size(475, 42)
$form.Controls.Add($subtitle)

$statusLabel = New-Object System.Windows.Forms.Label
$statusLabel.Text = '正在读取网络状态…'
$statusLabel.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 12, [System.Drawing.FontStyle]::Bold)
$statusLabel.ForeColor = [System.Drawing.Color]::FromArgb(33, 92, 158)
$statusLabel.BackColor = [System.Drawing.Color]::White
$statusLabel.BorderStyle = [System.Windows.Forms.BorderStyle]::FixedSingle
$statusLabel.TextAlign = [System.Drawing.ContentAlignment]::MiddleLeft
$statusLabel.Padding = New-Object System.Windows.Forms.Padding(14, 0, 8, 0)
$statusLabel.Location = New-Object System.Drawing.Point(28, 112)
$statusLabel.Size = New-Object System.Drawing.Size(468, 52)
$form.Controls.Add($statusLabel)

$adapterCaption = New-Object System.Windows.Forms.Label
$adapterCaption.Text = '网络'
$adapterCaption.Location = New-Object System.Drawing.Point(30, 184)
$adapterCaption.Size = New-Object System.Drawing.Size(120, 24)
$form.Controls.Add($adapterCaption)

$script:AdapterBox = New-Object System.Windows.Forms.ComboBox
$script:AdapterBox.DropDownStyle = [System.Windows.Forms.ComboBoxStyle]::DropDownList
$script:AdapterBox.Location = New-Object System.Drawing.Point(30, 210)
$script:AdapterBox.Size = New-Object System.Drawing.Size(462, 30)
$script:AdapterBox.DisplayMember = 'Name'
$form.Controls.Add($script:AdapterBox)

$serverCaption = New-Object System.Windows.Forms.Label
$serverCaption.Text = 'DNS64 服务'
$serverCaption.Location = New-Object System.Drawing.Point(30, 252)
$serverCaption.Size = New-Object System.Drawing.Size(150, 24)
$form.Controls.Add($serverCaption)

$script:ServerBox = New-Object System.Windows.Forms.ComboBox
$script:ServerBox.DropDownStyle = [System.Windows.Forms.ComboBoxStyle]::DropDownList
$script:ServerBox.Location = New-Object System.Drawing.Point(30, 278)
$script:ServerBox.Size = New-Object System.Drawing.Size(462, 30)
$script:ServerBox.DisplayMember = 'Name'
foreach ($server in $script:Servers) { [void]$script:ServerBox.Items.Add($server) }
$script:ServerBox.SelectedIndex = 0
$form.Controls.Add($script:ServerBox)

$script:ToggleButton = New-Object System.Windows.Forms.Button
$script:ToggleButton.Text = '一键开启 NAT64'
$script:ToggleButton.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 12, [System.Drawing.FontStyle]::Bold)
$script:ToggleButton.ForeColor = [System.Drawing.Color]::White
$script:ToggleButton.BackColor = [System.Drawing.Color]::FromArgb(30, 116, 210)
$script:ToggleButton.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
$script:ToggleButton.FlatAppearance.BorderSize = 0
$script:ToggleButton.Location = New-Object System.Drawing.Point(30, 330)
$script:ToggleButton.Size = New-Object System.Drawing.Size(462, 54)
$form.Controls.Add($script:ToggleButton)

$script:TestButton = New-Object System.Windows.Forms.Button
$script:TestButton.Text = '检查 NAT64 连通性'
$script:TestButton.Location = New-Object System.Drawing.Point(30, 398)
$script:TestButton.Size = New-Object System.Drawing.Size(210, 34)
$form.Controls.Add($script:TestButton)

$script:DetailsLabel = New-Object System.Windows.Forms.Label
$script:DetailsLabel.Text = 'DNS 查询会发送给所选公共服务商。校园网计费方式以学校规则为准。'
$script:DetailsLabel.ForeColor = [System.Drawing.Color]::FromArgb(92, 104, 119)
$script:DetailsLabel.Location = New-Object System.Drawing.Point(30, 442)
$script:DetailsLabel.Size = New-Object System.Drawing.Size(470, 36)
$form.Controls.Add($script:DetailsLabel)

$script:AdapterBox.Items.Clear()
foreach ($adapter in (Get-EligibleAdapters)) { [void]$script:AdapterBox.Items.Add($adapter) }
if ($script:AdapterBox.Items.Count -gt 0) { $script:AdapterBox.SelectedIndex = 0 }

function Refresh-View {
    $adapter = $script:AdapterBox.SelectedItem
    if ($null -eq $adapter) {
        $statusLabel.Text = '未找到带公网 IPv6 地址的已连接网卡'
        $statusLabel.ForeColor = [System.Drawing.Color]::FromArgb(176, 93, 21)
        $script:ToggleButton.Enabled = $false
        $script:TestButton.Enabled = $false
        $script:ServerBox.Enabled = $false
        return
    }

    try {
        $currentDns = @(Get-CurrentDns $adapter.InterfaceIndex)
        $active = @($currentDns | Where-Object { $_ -in @($script:Servers.Address) }).Count -gt 0
        $script:ToggleButton.Enabled = $true
        $script:TestButton.Enabled = $true
        $script:ServerBox.Enabled = -not $active
        if ($active) {
            $statusLabel.Text = "NAT64 已开启 · $($adapter.Name)"
            $statusLabel.ForeColor = [System.Drawing.Color]::FromArgb(24, 126, 74)
            $script:ToggleButton.Text = '一键恢复原设置'
            $script:ToggleButton.BackColor = [System.Drawing.Color]::FromArgb(90, 103, 120)
        } else {
            $statusLabel.Text = "尚未开启 · $($adapter.Name)"
            $statusLabel.ForeColor = [System.Drawing.Color]::FromArgb(33, 92, 158)
            $script:ToggleButton.Text = '一键开启 NAT64'
            $script:ToggleButton.BackColor = [System.Drawing.Color]::FromArgb(30, 116, 210)
        }
        $script:DetailsLabel.Text = "IPv6 DNS：$(if ($currentDns.Count) { $currentDns -join ', ' } else { '自动获取' })"
    } catch {
        $statusLabel.Text = '读取网络设置失败'
        $statusLabel.ForeColor = [System.Drawing.Color]::FromArgb(176, 58, 58)
        $script:DetailsLabel.Text = $_.Exception.GetBaseException().Message
    }
}

$script:AdapterBox.Add_SelectedIndexChanged({ Refresh-View })
$script:ToggleButton.Add_Click({
    $adapter = $script:AdapterBox.SelectedItem
    if ($null -eq $adapter) { return }
    $script:ToggleButton.Enabled = $false
    $form.UseWaitCursor = $true
    try {
        $currentDns = @(Get-CurrentDns $adapter.InterfaceIndex)
        $isEnabled = @($currentDns | Where-Object { $_ -in @($script:Servers.Address) }).Count -gt 0
        if ($isEnabled) {
            $saved = Get-SavedAdapterState $adapter
            if ($null -ne $saved -and @($saved.Servers).Count -gt 0) {
                Set-DnsClientServerAddress -InterfaceIndex $adapter.InterfaceIndex -AddressFamily IPv6 -ServerAddresses @($saved.Servers) -ErrorAction Stop
            } else {
                Set-DnsClientServerAddress -InterfaceIndex $adapter.InterfaceIndex -AddressFamily IPv6 -ResetServerAddresses -ErrorAction Stop
            }
            $remaining = @(Read-SavedState | Where-Object { $_.InterfaceGuid -ne $adapter.InterfaceGuid })
            Write-SavedState $remaining
            Clear-DnsClientCache
            [System.Windows.Forms.MessageBox]::Show('已恢复开启前保存的 IPv6 DNS 设置。', 'NAT64 Switch', 'OK', 'Information') | Out-Null
        } else {
            $entries = @(Read-SavedState)
            $saved = @($entries | Where-Object { $_.InterfaceGuid -eq $adapter.InterfaceGuid } | Select-Object -First 1)
            if ($saved.Count -eq 0) {
                $entries += [pscustomobject]@{
                    InterfaceGuid = $adapter.InterfaceGuid
                    InterfaceName = $adapter.Name
                    Servers = @($currentDns)
                    SavedAt = (Get-Date).ToString('o')
                }
                Write-SavedState $entries
            }
            $server = $script:ServerBox.SelectedItem
            Set-DnsClientServerAddress -InterfaceIndex $adapter.InterfaceIndex -AddressFamily IPv6 -ServerAddresses $server.Address -ErrorAction Stop
            Clear-DnsClientCache
            [System.Windows.Forms.MessageBox]::Show("已将 $($adapter.Name) 的 IPv6 DNS 切换到 $($server.Name)。", 'NAT64 Switch', 'OK', 'Information') | Out-Null
        }
    } catch {
        [System.Windows.Forms.MessageBox]::Show($_.Exception.GetBaseException().Message, '操作失败', 'OK', 'Error') | Out-Null
    } finally {
        $form.UseWaitCursor = $false
        Refresh-View
    }
})

$script:TestButton.Add_Click({
    $server = $script:ServerBox.SelectedItem
    if ($null -eq $server) { return }
    $script:TestButton.Enabled = $false
    $form.UseWaitCursor = $true
    try {
        $message = Test-Nat64 $server.Address
        [System.Windows.Forms.MessageBox]::Show($message, '连通性检查通过', 'OK', 'Information') | Out-Null
    } catch {
        [System.Windows.Forms.MessageBox]::Show($_.Exception.GetBaseException().Message, '连通性检查失败', 'OK', 'Warning') | Out-Null
    } finally {
        $form.UseWaitCursor = $false
        $script:TestButton.Enabled = $true
    }
})

Refresh-View
[void]$form.ShowDialog()
