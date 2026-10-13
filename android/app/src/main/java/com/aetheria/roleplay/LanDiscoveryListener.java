package com.aetheria.roleplay;

import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.nio.charset.StandardCharsets;

public class LanDiscoveryListener implements Runnable {

    public interface OnHostDiscoveredListener {
        void onHostDiscovered(String hostIp);
    }

    private static final int DISCOVERY_PORT = 47891;
    private final OnHostDiscoveredListener mListener;
    private boolean mRunning = false;
    private Thread mThread;

    public LanDiscoveryListener(OnHostDiscoveredListener listener) {
        mListener = listener;
    }

    public synchronized void start() {
        if (mRunning) return;
        mRunning = true;
        mThread = new Thread(this);
        mThread.start();
    }

    public synchronized void startListening() {
        start();
    }

    public synchronized void stopListening() {
        stop();
    }

    @Override
    public void run() {
        DatagramSocket socket = null;
        try {
            socket = new DatagramSocket();
            socket.setBroadcast(true);
            socket.setSoTimeout(3000);

            byte[] query = "AETHERIA_DISCOVER_V1".getBytes(StandardCharsets.UTF_8);
            DatagramPacket sendPacket = new DatagramPacket(
                    query,
                    query.length,
                    InetAddress.getByName("255.255.255.255"),
                    DISCOVERY_PORT
            );

            while (mRunning) {
                try {
                    socket.send(sendPacket);

                    byte[] buf = new byte[2048];
                    DatagramPacket recvPacket = new DatagramPacket(buf, buf.length);
                    socket.receive(recvPacket);

                    String hostIp = recvPacket.getAddress().getHostAddress();
                    if (hostIp != null && !hostIp.isEmpty()) {
                        mListener.onHostDiscovered(hostIp);
                        break;
                    }
                } catch (Exception ignored) {
                    // Timeout, try again after short delay
                }

                try {
                    Thread.sleep(4000);
                } catch (InterruptedException e) {
                    break;
                }
            }
        } catch (Exception e) {
            // Ignore UDP network errors
        } finally {
            if (socket != null && !socket.isClosed()) {
                socket.close();
            }
        }
    }

    public synchronized void stop() {
        mRunning = false;
        if (mThread != null) {
            mThread.interrupt();
            mThread = null;
        }
    }
}
