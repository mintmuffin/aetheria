package com.aetheria.roleplay.ai;

import android.os.Handler;
import android.os.Looper;

import com.aetheria.roleplay.models.CharacterModel;
import com.aetheria.roleplay.models.ChatMessageModel;
import com.aetheria.roleplay.models.PersonaModel;
import com.aetheria.roleplay.models.WorldModel;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.List;

public class AiChatEngine {

    public interface StreamListener {
        void onChunk(String chunk);
        void onComplete(String fullText);
        void onError(String error);
    }

    public interface TestListener {
        void onResult(boolean success, String message);
    }

    private final Handler mMainHandler = new Handler(Looper.getMainLooper());

    static class ChunkPoster implements Runnable {
        private final StreamListener mListener;
        private final String mChunk;
        ChunkPoster(StreamListener l, String c) { mListener = l; mChunk = c; }
        @Override public void run() { mListener.onChunk(mChunk); }
    }

    static class CompletePoster implements Runnable {
        private final StreamListener mListener;
        private final String mFullText;
        CompletePoster(StreamListener l, String f) { mListener = l; mFullText = f; }
        @Override public void run() { mListener.onComplete(mFullText); }
    }

    static class ErrorPoster implements Runnable {
        private final StreamListener mListener;
        private final String mError;
        ErrorPoster(StreamListener l, String e) { mListener = l; mError = e; }
        @Override public void run() { mListener.onError(mError); }
    }

    static class StreamWorkerRunnable implements Runnable {
        private final String mEndpoint;
        private final String mApiKey;
        private final String mModel;
        private final double mTemperature;
        private final int mMaxTokens;
        private final String mSystemPrompt;
        private final List<ChatMessageModel> mHistory;
        private final String mUserMessage;
        private final StreamListener mListener;
        private final Handler mHandler;

        StreamWorkerRunnable(String endpoint, String apiKey, String model, double temp, int maxTokens,
                             String systemPrompt, List<ChatMessageModel> history, String userMsg,
                             StreamListener listener, Handler handler) {
            mEndpoint = endpoint;
            mApiKey = apiKey;
            mModel = model;
            mTemperature = temp;
            mMaxTokens = maxTokens;
            mSystemPrompt = systemPrompt;
            mHistory = history;
            mUserMessage = userMsg;
            mListener = listener;
            mHandler = handler;
        }

        @Override
        public void run() {
            HttpURLConnection conn = null;
            final StringBuilder fullResponse = new StringBuilder();
            try {
                String urlStr = mEndpoint.trim();
                if (!urlStr.endsWith("/chat/completions")) {
                    if (urlStr.endsWith("/")) {
                        urlStr += "chat/completions";
                    } else {
                        urlStr += "/chat/completions";
                    }
                }

                URL url = new URL(urlStr);
                conn = (HttpURLConnection) url.openConnection();
                conn.setRequestMethod("POST");
                conn.setRequestProperty("Content-Type", "application/json; charset=UTF-8");
                conn.setRequestProperty("Accept", "text/event-stream, application/json");
                if (mApiKey != null && !mApiKey.isEmpty()) {
                    conn.setRequestProperty("Authorization", "Bearer " + mApiKey);
                }
                conn.setConnectTimeout(15000);
                conn.setReadTimeout(90000);
                conn.setDoOutput(true);
                conn.setDoInput(true);

                // Build JSON payload
                JSONObject payload = new JSONObject();
                payload.put("model", mModel);
                payload.put("temperature", mTemperature);
                payload.put("max_tokens", mMaxTokens);
                payload.put("stream", true);

                JSONArray messages = new JSONArray();

                // System message
                JSONObject sysObj = new JSONObject();
                sysObj.put("role", "system");
                sysObj.put("content", mSystemPrompt);
                messages.put(sysObj);

                // History messages (recent 16 turns)
                int startIdx = Math.max(0, mHistory.size() - 16);
                for (int i = startIdx; i < mHistory.size(); i++) {
                    ChatMessageModel m = mHistory.get(i);
                    JSONObject msgObj = new JSONObject();
                    msgObj.put("role", m.role);
                    msgObj.put("content", m.text);
                    messages.put(msgObj);
                }

                // New User message
                JSONObject userObj = new JSONObject();
                userObj.put("role", "user");
                userObj.put("content", mUserMessage);
                messages.put(userObj);

                payload.put("messages", messages);

                // Send request
                byte[] postData = payload.toString().getBytes(StandardCharsets.UTF_8);
                try (OutputStream os = conn.getOutputStream()) {
                    os.write(postData);
                    os.flush();
                }

                int code = conn.getResponseCode();
                if (code >= 200 && code < 300) {
                    InputStream is = conn.getInputStream();
                    BufferedReader reader = new BufferedReader(new InputStreamReader(is, StandardCharsets.UTF_8));
                    String line;
                    while ((line = reader.readLine()) != null) {
                        line = line.trim();
                        if (line.isEmpty() || line.startsWith(":")) continue;

                        if (line.startsWith("data:")) {
                            String data = line.substring(5).trim();
                            if ("[DONE]".equals(data)) break;

                            try {
                                JSONObject json = new JSONObject(data);
                                JSONArray choices = json.optJSONArray("choices");
                                if (choices != null && choices.length() > 0) {
                                    JSONObject choice = choices.getJSONObject(0);
                                    JSONObject delta = choice.optJSONObject("delta");
                                    if (delta != null && delta.has("content")) {
                                        String chunk = delta.getString("content");
                                        fullResponse.append(chunk);
                                        mHandler.post(new ChunkPoster(mListener, chunk));
                                    }
                                }
                            } catch (Exception ignored) { }
                        }
                    }
                    reader.close();

                    mHandler.post(new CompletePoster(mListener, fullResponse.toString()));

                } else {
                    // Read error stream
                    InputStream es = conn.getErrorStream();
                    String errBody = "";
                    if (es != null) {
                        BufferedReader r = new BufferedReader(new InputStreamReader(es, StandardCharsets.UTF_8));
                        StringBuilder sb = new StringBuilder();
                        String l;
                        while ((l = r.readLine()) != null) sb.append(l);
                        r.close();
                        errBody = sb.toString();
                    }
                    mHandler.post(new ErrorPoster(mListener, "HTTP " + code + ": " + errBody));
                }

            } catch (Exception e) {
                String msg = e.getMessage() != null ? e.getMessage() : e.toString();
                mHandler.post(new ErrorPoster(mListener, msg));
            } finally {
                if (conn != null) {
                    conn.disconnect();
                }
            }
        }
    }

    public void streamCompletion(String endpoint, String apiKey, String model, double temperature, int maxTokens,
                                 CharacterModel character, WorldModel world, PersonaModel persona,
                                 List<ChatMessageModel> history, String userMessage,
                                 StreamListener listener) {

        StringBuilder sys = new StringBuilder();
        sys.append("You are roleplaying as the following character in an immersive interactive story.\n\n");
        if (character != null) {
            sys.append("CHARACTER PROFILE:\n");
            sys.append("Name: ").append(character.name).append("\n");
            sys.append("Title: ").append(character.title).append("\n");
            sys.append("Personality: ").append(character.personality).append("\n");
            sys.append("Speech Style: ").append(character.speechStyle).append("\n");
            sys.append("Description: ").append(character.description).append("\n\n");
        }
        if (world != null) {
            sys.append("WORLD SETTING:\n");
            sys.append("World: ").append(world.name).append("\n");
            sys.append("Genre: ").append(world.genre).append("\n");
            sys.append("Setting: ").append(world.setting).append("\n");
            sys.append("Lore: ").append(world.loreSummary).append("\n\n");
        }
        if (persona != null) {
            sys.append("USER PERSONA (who the user is roleplaying as):\n");
            sys.append("Name: ").append(persona.name).append("\n");
            sys.append("Description: ").append(persona.description).append("\n\n");
        }
        sys.append("GUIDELINES:\n");
        sys.append("- Write expressive, immersive, multi-paragraph dialogue and descriptive actions.\n");
        sys.append("- Stay strictly faithful to your character's voice and established world lore.\n");
        sys.append("- Emphasize sensory details, genuine emotional depth, and reactive banter.\n");

        Thread t = new Thread(new StreamWorkerRunnable(
                endpoint, apiKey, model, temperature, maxTokens,
                sys.toString(), history, userMessage, listener, mMainHandler
        ));
        t.start();
    }

    static class TestResultPoster implements Runnable {
        private final TestListener mListener;
        private final boolean mSuccess;
        private final String mMessage;
        TestResultPoster(TestListener l, boolean s, String m) { mListener = l; mSuccess = s; mMessage = m; }
        @Override public void run() { mListener.onResult(mSuccess, mMessage); }
    }

    static class TestWorkerRunnable implements Runnable {
        private final String mEndpoint;
        private final String mApiKey;
        private final String mModel;
        private final TestListener mListener;
        private final Handler mHandler;

        TestWorkerRunnable(String endpoint, String apiKey, String model, TestListener listener, Handler handler) {
            mEndpoint = endpoint;
            mApiKey = apiKey;
            mModel = model;
            mListener = listener;
            mHandler = handler;
        }

        @Override
        public void run() {
            HttpURLConnection conn = null;
            try {
                String urlStr = mEndpoint.trim();
                if (!urlStr.endsWith("/chat/completions")) {
                    if (urlStr.endsWith("/")) {
                        urlStr += "chat/completions";
                    } else {
                        urlStr += "/chat/completions";
                    }
                }
                URL url = new URL(urlStr);
                conn = (HttpURLConnection) url.openConnection();
                conn.setRequestMethod("POST");
                conn.setRequestProperty("Content-Type", "application/json; charset=UTF-8");
                if (mApiKey != null && !mApiKey.isEmpty()) {
                    conn.setRequestProperty("Authorization", "Bearer " + mApiKey);
                }
                conn.setConnectTimeout(8000);
                conn.setReadTimeout(12000);
                conn.setDoOutput(true);

                JSONObject payload = new JSONObject();
                payload.put("model", mModel);
                payload.put("max_tokens", 5);
                payload.put("stream", false);

                JSONArray msgs = new JSONArray();
                JSONObject m = new JSONObject();
                m.put("role", "user");
                m.put("content", "ping");
                msgs.put(m);
                payload.put("messages", msgs);

                byte[] postData = payload.toString().getBytes(StandardCharsets.UTF_8);
                try (OutputStream os = conn.getOutputStream()) {
                    os.write(postData);
                    os.flush();
                }

                int code = conn.getResponseCode();
                boolean ok = (code >= 200 && code < 300);
                String msg = ok ? "Connected successfully! (HTTP " + code + ")" : "Connection failed: HTTP " + code;
                mHandler.post(new TestResultPoster(mListener, ok, msg));

            } catch (Exception e) {
                mHandler.post(new TestResultPoster(mListener, false, "Connection error: " + e.getMessage()));
            } finally {
                if (conn != null) conn.disconnect();
            }
        }
    }

    public void testConnection(String endpoint, String apiKey, String model, TestListener listener) {
        Thread t = new Thread(new TestWorkerRunnable(endpoint, apiKey, model, listener, mMainHandler));
        t.start();
    }
}
