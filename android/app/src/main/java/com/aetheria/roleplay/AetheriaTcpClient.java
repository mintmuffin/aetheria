package com.aetheria.roleplay;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.DataInputStream;
import java.io.DataOutputStream;
import java.io.IOException;
import java.net.InetSocketAddress;
import java.net.Socket;
import java.nio.charset.StandardCharsets;

public class AetheriaTcpClient {

    public static final int TYPE_HANDSHAKE_REQUEST = 0;
    public static final int TYPE_HANDSHAKE_ACCEPTED = 1;
    public static final int TYPE_HANDSHAKE_REJECTED = 2;
    public static final int TYPE_SESSION_SNAPSHOT = 3;
    public static final int TYPE_PLAYER_ACTION_SUBMIT = 4;
    public static final int TYPE_MESSAGE_BROADCAST = 5;
    public static final int TYPE_TURN_STATE_UPDATE = 6;
    public static final int TYPE_WORLD_STATE_UPDATE = 7;
    public static final int TYPE_AI_STREAM_START = 8;
    public static final int TYPE_AI_STREAM_CHUNK = 9;
    public static final int TYPE_AI_STREAM_END = 10;
    public static final int TYPE_PARTICIPANT_LIST_UPDATE = 11;
    public static final int TYPE_CHARACTER_SYNC = 12;
    public static final int TYPE_SYSTEM_NOTICE = 13;
    public static final int TYPE_PING = 14;
    public static final int TYPE_PONG = 15;
    public static final int TYPE_PARTICIPANT_UPDATE = 16;
    public static final int TYPE_LOBBY_STATE_UPDATE = 17;

    public interface OnSessionEventListener {
        void onConnected(String participantId);
        void onLobbyStateUpdated(String roomTitle, String gameMode, String worldName, boolean gameStarted, String participantsJson);
        void onGameLaunchReceived();
        void onParticipantsUpdated(String participantsJson);
        void onChatMessageReceived(String sender, String text, long timestamp, String actionType);
        void onAiStreamChunk(String sender, String delta);
        void onCharacterSynced(String charJson);
        void onDiceRolled(String roller, int sides, int total, String description);
        void onDisconnected(String reason);
        void onError(String error);
    }

    private Socket mSocket;
    private DataOutputStream mOut;
    private DataInputStream mIn;
    private boolean mConnected = false;
    private Thread mReadThread;
    private OnSessionEventListener mListener;
    private String mAssignedParticipantId = "";

    public AetheriaTcpClient() { }

    public AetheriaTcpClient(OnSessionEventListener listener) {
        mListener = listener;
    }

    public void setListener(OnSessionEventListener listener) {
        mListener = listener;
    }

    public String getAssignedParticipantId() {
        return mAssignedParticipantId;
    }

    static class ConnectWorker implements Runnable {
        private final AetheriaTcpClient mClient;
        private final String mHost;
        private final int mPort;
        private final String mInviteToken;
        private final String mPersonaName;

        ConnectWorker(AetheriaTcpClient client, String host, int port, String token, String persona) {
            mClient = client;
            mHost = host;
            mPort = port;
            mInviteToken = token;
            mPersonaName = persona;
        }

        @Override
        public void run() {
            try {
                mClient.mSocket = new Socket();
                mClient.mSocket.setTcpNoDelay(true);
                mClient.mSocket.connect(new InetSocketAddress(mHost, mPort), 6000);
                mClient.mOut = new DataOutputStream(mClient.mSocket.getOutputStream());
                mClient.mIn = new DataInputStream(mClient.mSocket.getInputStream());
                mClient.mConnected = true;

                // Send Handshake packet (Type: HandshakeRequest = 0)
                JSONObject handshakePayload = new JSONObject();
                handshakePayload.put("InviteToken", mInviteToken);
                handshakePayload.put("DisplayName", mPersonaName);
                handshakePayload.put("PersonaName", mPersonaName);

                JSONObject packet = new JSONObject();
                packet.put("Type", TYPE_HANDSHAKE_REQUEST);
                packet.put("SenderParticipantId", "android-" + System.currentTimeMillis());
                packet.put("PayloadJson", handshakePayload.toString());

                mClient.sendPacket(packet.toString());
                mClient.startReading();

            } catch (final Exception e) {
                mClient.disconnect();
                if (mClient.mListener != null) {
                    mClient.mListener.onError(e.getMessage() != null ? e.getMessage() : e.toString());
                }
            }
        }
    }

    public synchronized void connect(String host, int port, String inviteToken, String personaName) {
        disconnect();
        Thread t = new Thread(new ConnectWorker(this, host, port, inviteToken, personaName));
        t.start();
    }

    static class ReadWorker implements Runnable {
        private final AetheriaTcpClient mClient;

        ReadWorker(AetheriaTcpClient client) {
            mClient = client;
        }

        @Override
        public void run() {
            try {
                while (mClient.mConnected && mClient.mIn != null) {
                    int length = mClient.mIn.readInt();
                    if (length <= 0 || length > 4 * 1024 * 1024) break;
                    byte[] payload = new byte[length];
                    mClient.mIn.readFully(payload);
                    String jsonStr = new String(payload, StandardCharsets.UTF_8);
                    mClient.handleIncomingPacket(jsonStr);
                }
            } catch (Exception e) {
                if (mClient.mListener != null && mClient.mConnected) {
                    mClient.mListener.onDisconnected(e.getMessage() != null ? e.getMessage() : "Connection lost.");
                }
            } finally {
                mClient.disconnect();
            }
        }
    }

    private void startReading() {
        mReadThread = new Thread(new ReadWorker(this));
        mReadThread.start();
    }

    private void handleIncomingPacket(String jsonStr) {
        try {
            JSONObject packet = new JSONObject(jsonStr);
            int type = packet.optInt("Type", -1);
            String payloadJson = packet.optString("PayloadJson", "");

            if (type == TYPE_HANDSHAKE_ACCEPTED) { // 1
                JSONObject p = new JSONObject(payloadJson);
                mAssignedParticipantId = p.optString("AssignedParticipantId", "");
                if (mListener != null) {
                    mListener.onConnected(mAssignedParticipantId);
                }
            } else if (type == TYPE_HANDSHAKE_REJECTED) { // 2
                JSONObject p = new JSONObject(payloadJson);
                String reason = p.optString("Reason", "Handshake rejected.");
                if (mListener != null) {
                    mListener.onError("Host Rejected: " + reason);
                }
                disconnect();
            } else if (type == TYPE_MESSAGE_BROADCAST) { // 5
                JSONObject p = new JSONObject(payloadJson);
                String sender = p.optString("SenderName", "Player");
                String text = p.optString("Content", "");
                String actionType = p.optString("ActionType", "Speak");
                if (mListener != null) {
                    mListener.onChatMessageReceived(sender, text, System.currentTimeMillis(), actionType);
                }
            } else if (type == TYPE_AI_STREAM_CHUNK) { // 9
                JSONObject p = new JSONObject(payloadJson);
                String sender = p.optString("SenderName", "AI");
                String delta = p.optString("DeltaOrFullContent", "");
                if (mListener != null) {
                    mListener.onAiStreamChunk(sender, delta);
                }
            } else if (type == TYPE_PARTICIPANT_LIST_UPDATE) { // 11
                if (mListener != null) {
                    mListener.onParticipantsUpdated(payloadJson);
                }
            } else if (type == TYPE_CHARACTER_SYNC) { // 12
                if (mListener != null) {
                    mListener.onCharacterSynced(payloadJson);
                }
            } else if (type == TYPE_LOBBY_STATE_UPDATE) { // 17
                JSONObject p = new JSONObject(payloadJson);
                String title = p.optString("Title", "Room Lobby");
                String gameMode = p.optString("GameMode", "Freeform");
                String world = p.optString("WorldName", "Default World");
                boolean started = p.optBoolean("GameStarted", false);
                JSONArray parts = p.optJSONArray("Participants");
                String partsStr = parts != null ? parts.toString() : "[]";
                if (mListener != null) {
                    mListener.onLobbyStateUpdated(title, gameMode, world, started, partsStr);
                    if (started) {
                        mListener.onGameLaunchReceived();
                    }
                }
            } else if (type == TYPE_SYSTEM_NOTICE) { // 13
                if (mListener != null) {
                    mListener.onError(payloadJson);
                }
            }
        } catch (Exception ignored) { }
    }

    public synchronized void sendPacket(String jsonPayload) throws IOException {
        if (!mConnected || mOut == null) throw new IOException("Not connected to host.");
        byte[] bytes = jsonPayload.getBytes(StandardCharsets.UTF_8);
        mOut.writeInt(bytes.length);
        mOut.write(bytes);
        mOut.flush();
    }

    static class SendActionWorker implements Runnable {
        private final AetheriaTcpClient mClient;
        private final int mActionType;
        private final String mContent;

        SendActionWorker(AetheriaTcpClient client, int actionType, String content) {
            mClient = client;
            mActionType = actionType;
            mContent = content;
        }

        @Override
        public void run() {
            try {
                JSONObject actionPayload = new JSONObject();
                actionPayload.put("ActionType", mActionType);
                actionPayload.put("Content", mContent);
                actionPayload.put("RequestAiResponse", true);

                JSONObject packet = new JSONObject();
                packet.put("Type", TYPE_PLAYER_ACTION_SUBMIT); // 4
                packet.put("SenderParticipantId", mClient.mAssignedParticipantId);
                packet.put("PayloadJson", actionPayload.toString());

                mClient.sendPacket(packet.toString());
            } catch (Exception ignored) { }
        }
    }

    public void sendAction(int actionType, String content) {
        Thread t = new Thread(new SendActionWorker(this, actionType, content));
        t.start();
    }

    public void sendChatMessage(String text) {
        sendAction(0, text); // 0 = Speak
    }

    static class UpdateIdentityWorker implements Runnable {
        private final AetheriaTcpClient mClient;
        private final String mPersonaName;
        private final String mCharacterId;
        private final boolean mIsReady;

        UpdateIdentityWorker(AetheriaTcpClient client, String personaName, String characterId, boolean isReady) {
            mClient = client;
            mPersonaName = personaName;
            mCharacterId = characterId;
            mIsReady = isReady;
        }

        @Override
        public void run() {
            try {
                JSONObject payload = new JSONObject();
                payload.put("ParticipantId", mClient.mAssignedParticipantId);
                payload.put("DisplayName", mPersonaName);
                payload.put("PersonaName", mPersonaName);
                if (mCharacterId != null && !mCharacterId.isEmpty()) {
                    payload.put("CharacterId", mCharacterId);
                }
                payload.put("IsReady", mIsReady);

                JSONObject packet = new JSONObject();
                packet.put("Type", TYPE_PARTICIPANT_UPDATE); // 16
                packet.put("SenderParticipantId", mClient.mAssignedParticipantId);
                packet.put("PayloadJson", payload.toString());

                mClient.sendPacket(packet.toString());
            } catch (Exception ignored) { }
        }
    }

    public void updateIdentity(String personaName, String characterId, boolean isReady) {
        Thread t = new Thread(new UpdateIdentityWorker(this, personaName, characterId, isReady));
        t.start();
    }

    static class RollDiceWorker implements Runnable {
        private final AetheriaTcpClient mClient;
        private final int mSides;
        private final int mModifier;

        RollDiceWorker(AetheriaTcpClient client, int sides, int mod) {
            mClient = client;
            mSides = sides;
            mModifier = mod;
        }

        @Override
        public void run() {
            try {
                int roll = (int) (Math.random() * mSides) + 1 + mModifier;
                String desc = "*rolls a d" + mSides + (mModifier != 0 ? ("+" + mModifier) : "") + " -> Total: " + roll + "*";
                mClient.sendAction(0, desc); // Speak roll in character
            } catch (Exception ignored) { }
        }
    }

    public void rollDice(int sides, int modifier) {
        Thread t = new Thread(new RollDiceWorker(this, sides, modifier));
        t.start();
    }

    public synchronized void disconnect() {
        mConnected = false;
        try {
            if (mSocket != null && !mSocket.isClosed()) {
                mSocket.close();
            }
        } catch (Exception ignored) { }
        mSocket = null;
        mOut = null;
        mIn = null;
    }

    public boolean isConnected() {
        return mConnected;
    }
}
