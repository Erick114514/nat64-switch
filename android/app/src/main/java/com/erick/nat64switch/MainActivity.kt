package com.erick.nat64switch

import android.app.Activity
import android.content.Context
import android.content.Intent
import android.graphics.Color
import android.net.VpnService
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.view.Gravity
import android.view.View
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.Spinner
import android.widget.TextView
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.Inet6Address
import java.net.InetSocketAddress
import java.net.Socket
import java.util.concurrent.Executors

class MainActivity : Activity() {
    private data class Server(val label: String, val address: String)

    private val servers = listOf(
        Server("nat64.net · 阿姆斯特丹", "2a00:1098:2b::1"),
        Server("nat64.net · 纽伦堡", "2a01:4f8:c2c:123f::1"),
        Server("nat64.net · 赫尔辛基", "2a01:4f9:c010:3f02::1")
    )
    private val worker = Executors.newSingleThreadExecutor()
    private val handler = Handler(Looper.getMainLooper())
    private lateinit var status: TextView
    private lateinit var serverPicker: Spinner
    private lateinit var toggle: Button
    private lateinit var test: Button
    private val prefs by lazy { getSharedPreferences(Dns64VpnService.PREFS, Context.MODE_PRIVATE) }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        buildScreen()
        refresh()
    }

    override fun onResume() {
        super.onResume()
        refresh()
    }

    override fun onDestroy() {
        worker.shutdownNow()
        handler.removeCallbacksAndMessages(null)
        super.onDestroy()
    }

    private fun buildScreen() {
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(24), dp(32), dp(24), dp(24))
            setBackgroundColor(Color.rgb(246, 248, 251))
        }
        val scroll = ScrollView(this).apply { addView(root) }
        setContentView(scroll)

        root.addView(TextView(this).apply {
            text = "NAT64 Switch"
            textSize = 26f
            setTextColor(Color.rgb(27, 42, 65))
            setTypeface(typeface, android.graphics.Typeface.BOLD)
        }, matchWrap())

        root.addView(TextView(this).apply {
            text = "一键让支持系统 DNS 的 IPv4-only 网站尝试经 IPv6 访问。"
            textSize = 15f
            setTextColor(Color.rgb(92, 104, 119))
            setPadding(0, dp(8), 0, dp(20))
        }, matchWrap())

        status = TextView(this).apply {
            textSize = 16f
            setTextColor(Color.rgb(33, 92, 158))
            setBackgroundColor(Color.WHITE)
            setPadding(dp(14), dp(16), dp(14), dp(16))
        }
        root.addView(status, matchWrap())

        root.addView(TextView(this).apply {
            text = "DNS64 服务"
            textSize = 14f
            setTextColor(Color.rgb(38, 51, 68))
            setPadding(0, dp(22), 0, dp(6))
        }, matchWrap())

        serverPicker = Spinner(this).apply {
            adapter = ArrayAdapter(
                this@MainActivity,
                android.R.layout.simple_spinner_dropdown_item,
                servers.map { it.label }
            )
        }
        root.addView(serverPicker, matchWrap())

        toggle = Button(this).apply {
            text = "一键开启 NAT64"
            isAllCaps = false
            setTextColor(Color.WHITE)
            setBackgroundColor(Color.rgb(30, 116, 210))
            setOnClickListener { toggleVpn() }
        }
        val toggleParams = LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MATCH_PARENT,
            dp(58)
        ).apply { topMargin = dp(24) }
        root.addView(toggle, toggleParams)

        test = Button(this).apply {
            text = "检查 NAT64 连通性"
            isAllCaps = false
            setOnClickListener { runConnectivityCheck() }
        }
        root.addView(test, LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MATCH_PARENT,
            dp(48)
        ).apply { topMargin = dp(8) })

        root.addView(TextView(this).apply {
            text = "首次开启需确认 Android VPN 授权。应用仅代理 UDP DNS，其他流量不经过 VPN。系统专用 DNS、应用自带 DoH/DoT 或 IPv4 字面地址可能绕过。DNS 查询会发送给所选公共服务。"
            textSize = 13f
            setTextColor(Color.rgb(92, 104, 119))
            setPadding(0, dp(18), 0, 0)
        }, matchWrap())
    }

    private fun toggleVpn() {
        toggle.isEnabled = false
        if (prefs.getBoolean(Dns64VpnService.KEY_ACTIVE, false)) {
            val stop = Intent(this, Dns64VpnService::class.java).setAction(Dns64VpnService.ACTION_STOP)
            startServiceCompat(stop)
            status.text = "正在关闭…"
            handler.postDelayed({ refresh(); toggle.isEnabled = true }, 400)
            return
        }

        val prepare = VpnService.prepare(this)
        if (prepare != null) {
            startActivityForResult(prepare, REQUEST_VPN)
        } else {
            startVpnService()
        }
    }

    @Deprecated("The VPN consent flow currently returns through Activity.onActivityResult")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode == REQUEST_VPN) {
            if (resultCode == RESULT_OK) startVpnService()
            else {
                status.text = "未获得 VPN 授权，设置没有更改。"
                toggle.isEnabled = true
            }
        }
    }

    private fun startVpnService() {
        val selected = servers[serverPicker.selectedItemPosition]
        val intent = Intent(this, Dns64VpnService::class.java)
            .setAction(Dns64VpnService.ACTION_START)
            .putExtra(Dns64VpnService.EXTRA_SERVER, selected.address)
        startServiceCompat(intent)
        status.text = "正在连接 ${selected.label}…"
        handler.postDelayed({ refresh(); toggle.isEnabled = true }, 1000)
    }

    private fun startServiceCompat(intent: Intent) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) startForegroundService(intent)
        else startService(intent)
    }

    private fun refresh() {
        val active = prefs.getBoolean(Dns64VpnService.KEY_ACTIVE, false)
        val error = prefs.getString(Dns64VpnService.KEY_ERROR, null)
        val resolver = prefs.getString(Dns64VpnService.KEY_SERVER, null)
        status.text = when {
            active -> "NAT64 已开启 · $resolver"
            !error.isNullOrBlank() -> "未连接 · $error"
            else -> "尚未开启"
        }
        toggle.text = if (active) "一键关闭并恢复" else "一键开启 NAT64"
        toggle.setBackgroundColor(if (active) Color.rgb(90, 103, 120) else Color.rgb(30, 116, 210))
        serverPicker.isEnabled = !active
        test.isEnabled = active
    }

    private fun runConnectivityCheck() {
        test.isEnabled = false
        status.text = "正在检查 DNS64 和 NAT64…"
        val selected = servers[serverPicker.selectedItemPosition]
        worker.execute {
            val result = try {
                // Query the VPN's local DNS endpoint directly. Android's system resolver can
                // use Private DNS, cached results, or suppress the underlying DNS error, which
                // made this check report a misleading UnknownHostException.
                val discovery = queryIpv6Records("ipv4only.arpa")
                if (discovery.isEmpty()) {
                    throw IllegalStateException(
                        "VPN DNS 没有为 ipv4only.arpa 返回合成 AAAA 记录；请确认所选 DNS64 服务可用。"
                    )
                }
                val addresses = queryIpv6Records("ipv4.google.com")
                if (addresses.isEmpty()) {
                    throw IllegalStateException("VPN DNS 没有为 ipv4.google.com 返回 IPv6 地址。")
                }
                var connected = false
                for (address in addresses) {
                    try {
                        Socket().use { socket ->
                            socket.connect(InetSocketAddress(address, 443), 5000)
                            connected = true
                        }
                        if (connected) break
                    } catch (_: Exception) {
                    }
                }
                if (!connected) throw IllegalStateException("没有通过合成 IPv6 地址连接测试站点。")
                "DNS64/NAT64 连通性通过：已连接 ipv4.google.com:443。当前 DNS64：${selected.label}。"
            } catch (ex: Exception) {
                "检查失败：${ex.message ?: ex.javaClass.simpleName}"
            }
            handler.post {
                status.text = result
                test.isEnabled = prefs.getBoolean(Dns64VpnService.KEY_ACTIVE, false)
            }
        }
    }

    private fun queryIpv6Records(domain: String): List<Inet6Address> {
        val id = (System.nanoTime().toInt() and 0xffff)
        val request = makeDnsAaaaQuery(domain, id)
        val dnsServer = InetAddress.getByAddress(byteArrayOf(10, 64, 0, 2))
        val response = ByteArray(4096)

        DatagramSocket().use { socket ->
            socket.soTimeout = 5000
            socket.send(DatagramPacket(request, request.size, dnsServer, DNS_PORT))
            val answer = DatagramPacket(response, response.size)
            socket.receive(answer)
            return parseDnsAaaaAnswer(response.copyOf(answer.length), id)
        }
    }

    private fun makeDnsAaaaQuery(domain: String, id: Int): ByteArray {
        val labels = domain.split('.')
        val encodedName = ArrayList<Byte>()
        for (label in labels) {
            val bytes = label.toByteArray(Charsets.US_ASCII)
            require(bytes.isNotEmpty() && bytes.size <= 63) { "DNS 域名标签长度无效。" }
            encodedName.add(bytes.size.toByte())
            encodedName.addAll(bytes.toList())
        }
        encodedName.add(0)

        val query = ByteArray(12 + encodedName.size + 4)
        query[0] = (id ushr 8).toByte()
        query[1] = id.toByte()
        query[2] = 1 // Recursion desired.
        query[5] = 1 // One question.
        var offset = 12
        for (byte in encodedName) query[offset++] = byte
        query[offset++] = 0
        query[offset++] = 28 // AAAA.
        query[offset++] = 0
        query[offset] = 1 // IN.
        return query
    }

    private fun parseDnsAaaaAnswer(response: ByteArray, expectedId: Int): List<Inet6Address> {
        if (response.size < 12) throw IOException("DNS 响应过短。")
        val id = readU16(response, 0)
        if (id != expectedId) throw IOException("DNS 响应编号不匹配。")
        val flags = readU16(response, 2)
        if (flags and 0x8000 == 0) throw IOException("收到的不是 DNS 响应。")
        val rcode = flags and 0x000f
        if (rcode != 0) throw IOException("DNS 服务返回错误码 $rcode。")

        val questionCount = readU16(response, 4)
        val answerCount = readU16(response, 6)
        var offset = 12
        repeat(questionCount) {
            offset = skipDnsName(response, offset)
            if (offset + 4 > response.size) throw IOException("DNS 问题区不完整。")
            offset += 4
        }

        val addresses = mutableListOf<Inet6Address>()
        repeat(answerCount) {
            offset = skipDnsName(response, offset)
            if (offset + 10 > response.size) throw IOException("DNS 答案区不完整。")
            val type = readU16(response, offset)
            val recordClass = readU16(response, offset + 2)
            val dataLength = readU16(response, offset + 8)
            offset += 10
            if (offset + dataLength > response.size) throw IOException("DNS 记录长度无效。")
            if (type == 28 && recordClass == 1 && dataLength == 16) {
                val address = InetAddress.getByAddress(response.copyOfRange(offset, offset + 16))
                if (address is Inet6Address) addresses.add(address)
            }
            offset += dataLength
        }
        return addresses
    }

    private fun skipDnsName(packet: ByteArray, start: Int): Int {
        var offset = start
        while (true) {
            if (offset >= packet.size) throw IOException("DNS 域名字段不完整。")
            val length = packet[offset].toInt() and 0xff
            when {
                length == 0 -> return offset + 1
                length and 0xc0 == 0xc0 -> {
                    if (offset + 1 >= packet.size) throw IOException("DNS 压缩指针不完整。")
                    return offset + 2
                }
                length and 0xc0 != 0 -> throw IOException("DNS 域名编码无效。")
                else -> {
                    offset += length + 1
                    if (offset > packet.size) throw IOException("DNS 域名字段不完整。")
                }
            }
        }
    }

    private fun readU16(bytes: ByteArray, offset: Int): Int =
        ((bytes[offset].toInt() and 0xff) shl 8) or (bytes[offset + 1].toInt() and 0xff)

    private fun dp(value: Int): Int = (value * resources.displayMetrics.density).toInt()

    private fun matchWrap() = LinearLayout.LayoutParams(
        LinearLayout.LayoutParams.MATCH_PARENT,
        LinearLayout.LayoutParams.WRAP_CONTENT
    )

    companion object {
        private const val REQUEST_VPN = 31
        private const val DNS_PORT = 53
    }
}
