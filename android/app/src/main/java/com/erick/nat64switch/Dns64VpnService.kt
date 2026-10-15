package com.erick.nat64switch

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.net.VpnService
import android.net.Network
import android.os.Build
import android.os.ParcelFileDescriptor
import android.content.pm.ServiceInfo
import java.io.FileInputStream
import java.io.FileOutputStream
import java.io.DataInputStream
import java.io.DataOutputStream
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.Inet6Address
import java.net.InetSocketAddress
import java.net.Socket
import java.util.concurrent.atomic.AtomicBoolean

class Dns64VpnService : VpnService() {
    @Volatile private var running = AtomicBoolean(false)
    @Volatile private var tunnel: ParcelFileDescriptor? = null
    @Volatile private var worker: Thread? = null
    @Volatile private var upstream = DEFAULT_SERVER
    @Volatile private var upstreamNetwork: Network? = null
    @Volatile private var upstreamScopeId = -1

    private val prefs by lazy { getSharedPreferences(PREFS, Context.MODE_PRIVATE) }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) {
            stopTunnel()
            return START_NOT_STICKY
        }
        if (intent?.action != ACTION_START) return START_NOT_STICKY

        upstream = intent.getStringExtra(EXTRA_SERVER) ?: DEFAULT_SERVER
        upstreamNetwork = if (Build.VERSION.SDK_INT >= 33) {
            intent.getParcelableExtra(EXTRA_NETWORK, Network::class.java)
        } else {
            @Suppress("DEPRECATION")
            intent.getParcelableExtra(EXTRA_NETWORK)
        }
        upstreamScopeId = intent.getIntExtra(EXTRA_SERVER_SCOPE_ID, -1)
        prefs.edit().remove(KEY_ERROR).apply()
        try {
            enterForeground()
            establishTunnel()
        } catch (ex: Exception) {
            prefs.edit()
                .putBoolean(KEY_ACTIVE, false)
                .putString(KEY_ERROR, ex.message ?: ex.javaClass.simpleName)
                .apply()
            stopForeground(STOP_FOREGROUND_REMOVE)
            stopSelf(startId)
        }
        return START_NOT_STICKY
    }

    private fun enterForeground() {
        val manager = getSystemService(NotificationManager::class.java)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            manager.createNotificationChannel(
                NotificationChannel(CHANNEL_ID, "NAT64 Switch", NotificationManager.IMPORTANCE_LOW)
            )
        }
        val stopIntent = Intent(this, Dns64VpnService::class.java).setAction(ACTION_STOP)
        val stopPending = PendingIntent.getService(
            this,
            1,
            stopIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        val notification = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            Notification.Builder(this, CHANNEL_ID)
        } else {
            @Suppress("DEPRECATION")
            Notification.Builder(this)
        }.setSmallIcon(android.R.drawable.stat_sys_warning)
            .setContentTitle("NAT64 Switch 已开启")
            .setContentText("DNS64 转发运行中 · 点击停止可关闭")
            .setOngoing(true)
            .addAction(0, "停止", stopPending)
            .build()

        if (Build.VERSION.SDK_INT >= 34) {
            startForeground(
                NOTIFICATION_ID,
                notification,
                ServiceInfo.FOREGROUND_SERVICE_TYPE_SYSTEM_EXEMPTED
            )
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }
    }

    private fun establishTunnel() {
        if (running.get()) stopTunnel()
        val descriptor = Builder()
            .setSession("NAT64 Switch DNS")
            .setMtu(1500)
            .addAddress(TUN_ADDRESS, 32)
            .addDnsServer(DNS_ADDRESS)
            .addRoute(DNS_ADDRESS, 32)
            .establish() ?: throw IllegalStateException("Android 没有建立 VPN 接口。")

        tunnel = descriptor
        running.set(true)
        prefs.edit()
            .putBoolean(KEY_ACTIVE, true)
            .putString(KEY_SERVER, upstream)
            .remove(KEY_ERROR)
            .apply()
        worker = Thread({ proxyDns(descriptor) }, "nat64-dns-proxy").apply { start() }
    }

    private fun proxyDns(descriptor: ParcelFileDescriptor) {
        val input = FileInputStream(descriptor.fileDescriptor)
        val output = FileOutputStream(descriptor.fileDescriptor)
        val packet = ByteArray(32768)
        try {
            while (running.get()) {
                val length = input.read(packet)
                if (length <= 0) continue
                val request = parseUdpDnsRequest(packet, length) ?: continue
                val response = forwardDns(request.payload) ?: continue
                val reply = makeIpv4UdpReply(packet, request, response)
                synchronized(output) {
                    output.write(reply)
                    output.flush()
                }
            }
        } catch (ex: Exception) {
            if (running.get()) {
                prefs.edit()
                    .putBoolean(KEY_ACTIVE, false)
                    .putString(KEY_ERROR, "DNS 转发已停止：${ex.message ?: ex.javaClass.simpleName}")
                    .apply()
                stopTunnel()
            }
        } finally {
            try { input.close() } catch (_: Exception) { }
            try { output.close() } catch (_: Exception) { }
        }
    }

    private data class DnsRequest(
        val headerLength: Int,
        val sourcePort: Int,
        val payload: ByteArray
    )

    private fun parseUdpDnsRequest(packet: ByteArray, length: Int): DnsRequest? {
        if (length < 28 || (packet[0].toInt() ushr 4 and 0x0f) != 4) return null
        val ipHeaderLength = (packet[0].toInt() and 0x0f) * 4
        if (ipHeaderLength < 20 || length < ipHeaderLength + 8 || packet[9].toInt() and 0xff != 17) return null
        if ((packet[16].toInt() and 0xff) != 10 ||
            (packet[17].toInt() and 0xff) != 64 ||
            (packet[18].toInt() and 0xff) != 0 ||
            (packet[19].toInt() and 0xff) != 2) return null

        val sourcePort = readU16(packet, ipHeaderLength + 0)
        val destinationPort = readU16(packet, ipHeaderLength + 2)
        val udpLength = readU16(packet, ipHeaderLength + 4)
        if (destinationPort != 53 || udpLength < 8 || ipHeaderLength + udpLength > length) return null
        val dnsLength = udpLength - 8
        if (dnsLength < 12) return null
        return DnsRequest(
            headerLength = ipHeaderLength,
            sourcePort = sourcePort,
            payload = packet.copyOfRange(ipHeaderLength + 8, ipHeaderLength + 8 + dnsLength)
        )
    }

    private fun forwardDns(query: ByteArray): ByteArray? {
        val resolved = try {
            val address = InetAddress.getByName(upstream)
            if (address is Inet6Address && address.isLinkLocalAddress && upstreamScopeId >= 0) {
                Inet6Address.getByAddress(null, address.address, upstreamScopeId)
            } else address
        } catch (_: Exception) { return null }
        val remote = InetSocketAddress(resolved, DNS_PORT)
        try {
            DatagramSocket().use { socket ->
                if (!protect(socket)) throw IllegalStateException("无法保护 DNS 上游连接。")
                upstreamNetwork?.bindSocket(socket)
                socket.soTimeout = 2500
                socket.send(DatagramPacket(query, query.size, remote))
                val data = ByteArray(8192)
                val answer = DatagramPacket(data, data.size)
                socket.receive(answer)
                return data.copyOf(answer.length)
            }
        } catch (_: Exception) { /* Try TCP DNS when UDP/53 is filtered. */ }
        return try {
            Socket().use { socket ->
                if (!protect(socket)) throw IllegalStateException("无法保护 DNS 上游连接。")
                upstreamNetwork?.bindSocket(socket)
                socket.connect(remote, 2500)
                socket.soTimeout = 2500
                val output = DataOutputStream(socket.getOutputStream())
                output.writeShort(query.size)
                output.write(query)
                output.flush()
                val input = DataInputStream(socket.getInputStream())
                val length = input.readUnsignedShort()
                if (length < 12 || length > 8192) return null
                ByteArray(length).also { input.readFully(it) }
            }
        } catch (_: Exception) { null }
    }

    private fun makeIpv4UdpReply(original: ByteArray, request: DnsRequest, dns: ByteArray): ByteArray {
        val ipLength = 20
        val udpLength = 8 + dns.size
        val packet = ByteArray(ipLength + udpLength)
        packet[0] = 0x45
        writeU16(packet, 2, packet.size)
        packet[8] = 64
        packet[9] = 17
        packet[12] = 10
        packet[13] = 64
        packet[14] = 0
        packet[15] = 2
        System.arraycopy(original, 12, packet, 16, 4)
        writeU16(packet, 10, checksum(packet, 0, ipLength))

        writeU16(packet, ipLength, DNS_PORT)
        writeU16(packet, ipLength + 2, request.sourcePort)
        writeU16(packet, ipLength + 4, udpLength)
        // A zero UDP checksum is valid for IPv4 and keeps this small DNS proxy simple.
        System.arraycopy(dns, 0, packet, ipLength + 8, dns.size)
        return packet
    }

    private fun readU16(bytes: ByteArray, offset: Int): Int =
        ((bytes[offset].toInt() and 0xff) shl 8) or (bytes[offset + 1].toInt() and 0xff)

    private fun writeU16(bytes: ByteArray, offset: Int, value: Int) {
        bytes[offset] = (value ushr 8).toByte()
        bytes[offset + 1] = value.toByte()
    }

    private fun checksum(bytes: ByteArray, offset: Int, length: Int): Int {
        var sum = 0L
        var index = offset
        val end = offset + length
        while (index + 1 < end) {
            sum += ((bytes[index].toInt() and 0xff) shl 8) or (bytes[index + 1].toInt() and 0xff)
            index += 2
        }
        if (index < end) sum += (bytes[index].toInt() and 0xff) shl 8
        while ((sum ushr 16) != 0L) sum = (sum and 0xffff) + (sum ushr 16)
        return sum.inv().toInt() and 0xffff
    }

    private fun stopTunnel() {
        if (!running.getAndSet(false) && tunnel == null) {
            stopForeground(STOP_FOREGROUND_REMOVE)
            stopSelf()
            return
        }
        try { tunnel?.close() } catch (_: Exception) { }
        tunnel = null
        worker?.interrupt()
        worker = null
        prefs.edit().putBoolean(KEY_ACTIVE, false).apply()
        stopForeground(STOP_FOREGROUND_REMOVE)
        stopSelf()
    }

    override fun onRevoke() {
        stopTunnel()
        super.onRevoke()
    }

    override fun onDestroy() {
        running.set(false)
        try { tunnel?.close() } catch (_: Exception) { }
        tunnel = null
        prefs.edit().putBoolean(KEY_ACTIVE, false).apply()
        super.onDestroy()
    }

    companion object {
        const val PREFS = "nat64-switch"
        const val KEY_ACTIVE = "active"
        const val KEY_SERVER = "server"
        const val KEY_ERROR = "error"
        const val ACTION_START = "com.erick.nat64switch.START"
        const val ACTION_STOP = "com.erick.nat64switch.STOP"
        const val EXTRA_SERVER = "server"
        const val EXTRA_NETWORK = "network"
        const val EXTRA_SERVER_SCOPE_ID = "server-scope-id"
        private const val DEFAULT_SERVER = "2a00:1098:2b::1"
        private const val TUN_ADDRESS = "10.64.0.1"
        private const val DNS_ADDRESS = "10.64.0.2"
        private const val DNS_PORT = 53
        private const val CHANNEL_ID = "nat64-switch-active"
        private const val NOTIFICATION_ID = 6401
    }
}
