package com.aetheria.roleplay;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.Context;
import android.content.DialogInterface;
import android.graphics.Color;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.Vibrator;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.BaseAdapter;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ListView;
import android.widget.TextView;
import android.widget.Toast;

import com.aetheria.roleplay.ai.AiChatEngine;
import com.aetheria.roleplay.data.DatabaseHelper;
import com.aetheria.roleplay.models.CharacterModel;
import com.aetheria.roleplay.models.ChatMessageModel;
import com.aetheria.roleplay.models.PersonaModel;
import com.aetheria.roleplay.models.WorldModel;
import com.aetheria.roleplay.ui.DiceRoller;

import org.json.JSONArray;
import org.json.JSONObject;

import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.List;
import java.util.Locale;

public class MainActivity extends Activity implements View.OnClickListener, LanDiscoveryListener.OnHostDiscoveredListener, AetheriaTcpClient.OnSessionEventListener {

    // UI - Top Bar
    private TextView mTvAvatar;
    private TextView mTvTopTitle;
    private TextView mTvTopSubtitle;
    private TextView mTvModeBadge;
    private TextView mBtnQuickDice;

    // UI - Pages
    private View mPageRoleplay;
    private View mPageCharacters;
    private View mPageWorlds;
    private View mPagePersonas;
    private View mPageMultiplayer;
    private View mPageSettings;

    // UI - Navigation Tabs
    private TextView mTvNavRoleplay;
    private TextView mTvNavCharacters;
    private TextView mTvNavWorlds;
    private TextView mTvNavPersonas;
    private TextView mTvNavMultiplayer;
    private TextView mTvNavSettings;

    // UI - Roleplay Page
    private ListView mLvMessages;
    private TextView mTvTypingStatus;
    private EditText mEtMessageInput;
    private Button mBtnSend;
    private TextView mBtnInputDice;
    private MessagesAdapter mMessagesAdapter;

    // UI - Characters, Worlds, Personas
    private ListView mLvCharacters;
    private ListView mLvWorlds;
    private ListView mLvPersonas;
    private CharactersAdapter mCharactersAdapter;
    private WorldsAdapter mWorldsAdapter;
    private PersonasAdapter mPersonasAdapter;

    // UI - Multiplayer Page
    private TextView mTvCoopStatusDot;
    private TextView mTvCoopStatusText;
    private TextView mTvCoopDetails;
    private EditText mEtCoopHost;
    private EditText mEtCoopPort;
    private EditText mEtCoopToken;
    private Button mBtnCoopScan;
    private Button mBtnCoopConnect;

    // UI - Multiplayer Party Lobby
    private View mLlLobbySection;
    private TextView mTvLobbyRoomTitle;
    private TextView mTvLobbyMeta;
    private TextView mTvLobbySelectedRole;
    private TextView mTvLobbyCharPreview;
    private Button mBtnLobbyChangeCharacter;
    private Button mBtnLobbyReady;
    private Button mBtnLobbyEnterRoleplay;
    private LinearLayout mLlLobbyPartyMembers;
    private String mCurrentLobbyRole = "Reze";
    private boolean mIsLobbyReady = false;

    // UI - Settings Page
    private EditText mEtAiEndpoint;
    private EditText mEtAiKey;
    private EditText mEtAiModel;
    private EditText mEtAiTemp;
    private EditText mEtAiMaxTokens;
    private Button mBtnAiTest;
    private Button mBtnAiSave;
    private TextView mTvAiTestResult;

    // Backend Services & State
    private DatabaseHelper mDb;
    private final AiChatEngine mAiEngine = new AiChatEngine();
    private final Handler mMainHandler = new Handler(Looper.getMainLooper());
    private LanDiscoveryListener mLanDiscovery;
    private AetheriaTcpClient mTcpClient;

    private final List<ChatMessageModel> mCurrentMessages = new ArrayList<>();
    private final List<CharacterModel> mAllCharacters = new ArrayList<>();
    private final List<WorldModel> mAllWorlds = new ArrayList<>();
    private final List<PersonaModel> mAllPersonas = new ArrayList<>();

    private CharacterModel mActiveCharacter;
    private WorldModel mActiveWorld;
    private PersonaModel mActivePersona;
    private boolean mIsMultiplayerConnected = false;

    // Streaming State
    private boolean mIsStreaming = false;
    private ChatMessageModel mStreamingMsg = null;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);

        mDb = new DatabaseHelper(this);
        initViews();
        loadAllData();
        setupMultiplayer();
    }

    private void initViews() {
        // Top Bar
        mTvAvatar = findViewById(R.id.tv_avatar);
        mTvTopTitle = findViewById(R.id.tv_top_title);
        mTvTopSubtitle = findViewById(R.id.tv_top_subtitle);
        mTvModeBadge = findViewById(R.id.tv_mode_badge);
        mBtnQuickDice = findViewById(R.id.btn_quick_dice);
        mBtnQuickDice.setOnClickListener(this);

        // Pages
        mPageRoleplay = findViewById(R.id.page_roleplay);
        mPageCharacters = findViewById(R.id.page_characters);
        mPageWorlds = findViewById(R.id.page_worlds);
        mPagePersonas = findViewById(R.id.page_personas);
        mPageMultiplayer = findViewById(R.id.page_multiplayer);
        mPageSettings = findViewById(R.id.page_settings);

        // Navigation
        findViewById(R.id.nav_tab_roleplay).setOnClickListener(this);
        findViewById(R.id.nav_tab_characters).setOnClickListener(this);
        findViewById(R.id.nav_tab_worlds).setOnClickListener(this);
        findViewById(R.id.nav_tab_personas).setOnClickListener(this);
        findViewById(R.id.nav_tab_multiplayer).setOnClickListener(this);
        findViewById(R.id.nav_tab_settings).setOnClickListener(this);

        mTvNavRoleplay = findViewById(R.id.tv_nav_roleplay);
        mTvNavCharacters = findViewById(R.id.tv_nav_characters);
        mTvNavWorlds = findViewById(R.id.tv_nav_worlds);
        mTvNavPersonas = findViewById(R.id.tv_nav_personas);
        mTvNavMultiplayer = findViewById(R.id.tv_nav_multiplayer);
        mTvNavSettings = findViewById(R.id.tv_nav_settings);

        // Roleplay Page
        mLvMessages = findViewById(R.id.lv_messages);
        mTvTypingStatus = findViewById(R.id.tv_typing_status);
        mEtMessageInput = findViewById(R.id.et_message_input);
        mBtnSend = findViewById(R.id.btn_send);
        mBtnInputDice = findViewById(R.id.btn_input_dice);

        mBtnSend.setOnClickListener(this);
        mBtnInputDice.setOnClickListener(this);

        mMessagesAdapter = new MessagesAdapter(this, mCurrentMessages);
        mLvMessages.setAdapter(mMessagesAdapter);

        // Action Chips
        findViewById(R.id.chip_inspect).setOnClickListener(this);
        findViewById(R.id.chip_whisper).setOnClickListener(this);
        findViewById(R.id.chip_action).setOnClickListener(this);
        findViewById(R.id.chip_attack).setOnClickListener(this);
        findViewById(R.id.chip_d20).setOnClickListener(this);
        findViewById(R.id.chip_clear).setOnClickListener(this);

        // Characters, Worlds, Personas Lists
        findViewById(R.id.btn_new_character).setOnClickListener(this);
        findViewById(R.id.btn_new_persona).setOnClickListener(this);

        mLvCharacters = findViewById(R.id.lv_characters);
        mCharactersAdapter = new CharactersAdapter(this, mAllCharacters);
        mLvCharacters.setAdapter(mCharactersAdapter);

        mLvWorlds = findViewById(R.id.lv_worlds);
        mWorldsAdapter = new WorldsAdapter(this, mAllWorlds);
        mLvWorlds.setAdapter(mWorldsAdapter);

        mLvPersonas = findViewById(R.id.lv_personas);
        mPersonasAdapter = new PersonasAdapter(this, mAllPersonas);
        mLvPersonas.setAdapter(mPersonasAdapter);

        // Multiplayer Page
        mTvCoopStatusDot = findViewById(R.id.tv_coop_status_dot);
        mTvCoopStatusText = findViewById(R.id.tv_coop_status_text);
        mTvCoopDetails = findViewById(R.id.tv_coop_details);
        mEtCoopHost = findViewById(R.id.et_coop_host);
        mEtCoopPort = findViewById(R.id.et_coop_port);
        mEtCoopToken = findViewById(R.id.et_coop_token);
        mBtnCoopScan = findViewById(R.id.btn_coop_scan);
        mBtnCoopConnect = findViewById(R.id.btn_coop_connect);

        mBtnCoopScan.setOnClickListener(this);
        mBtnCoopConnect.setOnClickListener(this);

        // Multiplayer Party Lobby Views
        mLlLobbySection = findViewById(R.id.ll_coop_lobby_section);
        mTvLobbyRoomTitle = findViewById(R.id.tv_lobby_room_title);
        mTvLobbyMeta = findViewById(R.id.tv_lobby_meta);
        mTvLobbySelectedRole = findViewById(R.id.tv_lobby_selected_role);
        mTvLobbyCharPreview = findViewById(R.id.tv_lobby_char_preview);
        mBtnLobbyChangeCharacter = findViewById(R.id.btn_lobby_change_character);
        mBtnLobbyReady = findViewById(R.id.btn_lobby_ready);
        mBtnLobbyEnterRoleplay = findViewById(R.id.btn_lobby_enter_roleplay);
        mLlLobbyPartyMembers = findViewById(R.id.ll_lobby_party_members);

        if (mBtnLobbyChangeCharacter != null) mBtnLobbyChangeCharacter.setOnClickListener(this);
        if (mBtnLobbyReady != null) mBtnLobbyReady.setOnClickListener(this);
        if (mBtnLobbyEnterRoleplay != null) mBtnLobbyEnterRoleplay.setOnClickListener(this);

        // Settings Page
        mEtAiEndpoint = findViewById(R.id.et_ai_endpoint);
        mEtAiKey = findViewById(R.id.et_ai_key);
        mEtAiModel = findViewById(R.id.et_ai_model);
        mEtAiTemp = findViewById(R.id.et_ai_temperature);
        mEtAiMaxTokens = findViewById(R.id.et_ai_max_tokens);
        mBtnAiTest = findViewById(R.id.btn_ai_test);
        mBtnAiSave = findViewById(R.id.btn_ai_save);
        mTvAiTestResult = findViewById(R.id.tv_ai_test_result);

        mBtnAiTest.setOnClickListener(this);
        mBtnAiSave.setOnClickListener(this);
    }

    private void loadAllData() {
        // Load settings
        mEtAiEndpoint.setText(mDb.getConfig("endpoint", "https://openrouter.ai/api/v1"));
        mEtAiKey.setText(mDb.getConfig("api_key", ""));
        mEtAiModel.setText(mDb.getConfig("model", "sao10k/l3-8b-stheno-v3.2"));
        mEtAiTemp.setText(mDb.getConfig("temperature", "0.82"));
        mEtAiMaxTokens.setText(mDb.getConfig("max_tokens", "700"));

        // Load Characters
        mAllCharacters.clear();
        mAllCharacters.addAll(mDb.getAllCharacters());
        mCharactersAdapter.notifyDataSetChanged();

        if (!mAllCharacters.isEmpty()) {
            mActiveCharacter = mAllCharacters.get(0);
        }

        // Load Worlds
        mAllWorlds.clear();
        mAllWorlds.addAll(mDb.getAllWorlds());
        mWorldsAdapter.notifyDataSetChanged();
        if (!mAllWorlds.isEmpty()) {
            mActiveWorld = mAllWorlds.get(0);
        }

        // Load Personas
        mAllPersonas.clear();
        mAllPersonas.addAll(mDb.getAllPersonas());
        mPersonasAdapter.notifyDataSetChanged();
        for (PersonaModel p : mAllPersonas) {
            if (p.isDefault) {
                mActivePersona = p;
                break;
            }
        }
        if (mActivePersona == null && !mAllPersonas.isEmpty()) {
            mActivePersona = mAllPersonas.get(0);
        }

        updateTopBar();
        loadChatForActiveCharacter();
    }

    private void updateTopBar() {
        if (mActiveCharacter != null) {
            mTvTopTitle.setText(mActiveCharacter.name);
            mTvAvatar.setText(mActiveCharacter.name.substring(0, 1).toUpperCase());
        }
        if (mActiveWorld != null) {
            mTvTopSubtitle.setText(mActiveWorld.name);
        }
        if (mIsMultiplayerConnected) {
            mTvModeBadge.setText("LAN Co-Op");
            mTvModeBadge.setTextColor(Color.parseColor("#10B981"));
        } else {
            mTvModeBadge.setText("Solo AI");
            mTvModeBadge.setTextColor(Color.parseColor("#D0BCFF"));
        }
    }

    private void loadChatForActiveCharacter() {
        if (mActiveCharacter == null) return;
        mCurrentMessages.clear();
        List<ChatMessageModel> msgs = mDb.getMessagesForCharacter(mActiveCharacter.id);
        if (msgs.isEmpty() && mActiveCharacter.firstMessage != null && !mActiveCharacter.firstMessage.isEmpty()) {
            ChatMessageModel first = new ChatMessageModel(
                    0, mActiveCharacter.name, "assistant",
                    mActiveCharacter.firstMessage, System.currentTimeMillis(), "chat"
            );
            long id = mDb.saveMessage(mActiveCharacter.id, first);
            first.id = id;
            mCurrentMessages.add(first);
        } else {
            mCurrentMessages.addAll(msgs);
        }
        mMessagesAdapter.notifyDataSetChanged();
        scrollChatToBottom();
    }

    static class ScrollChatRunnable implements Runnable {
        private final ListView mLv;
        private final BaseAdapter mAd;
        ScrollChatRunnable(ListView lv, BaseAdapter ad) { mLv = lv; mAd = ad; }
        @Override public void run() { mLv.setSelection(mAd.getCount() - 1); }
    }

    private void scrollChatToBottom() {
        mLvMessages.post(new ScrollChatRunnable(mLvMessages, mMessagesAdapter));
    }

    private void switchTab(int pageIndex) {
        mPageRoleplay.setVisibility(pageIndex == 0 ? View.VISIBLE : View.GONE);
        mPageCharacters.setVisibility(pageIndex == 1 ? View.VISIBLE : View.GONE);
        mPageWorlds.setVisibility(pageIndex == 2 ? View.VISIBLE : View.GONE);
        mPagePersonas.setVisibility(pageIndex == 3 ? View.VISIBLE : View.GONE);
        mPageMultiplayer.setVisibility(pageIndex == 4 ? View.VISIBLE : View.GONE);
        mPageSettings.setVisibility(pageIndex == 5 ? View.VISIBLE : View.GONE);

        int activeColor = Color.parseColor("#D0BCFF");
        int inactiveColor = Color.parseColor("#CAC4D0");

        mTvNavRoleplay.setTextColor(pageIndex == 0 ? activeColor : inactiveColor);
        mTvNavCharacters.setTextColor(pageIndex == 1 ? activeColor : inactiveColor);
        mTvNavWorlds.setTextColor(pageIndex == 2 ? activeColor : inactiveColor);
        mTvNavPersonas.setTextColor(pageIndex == 3 ? activeColor : inactiveColor);
        mTvNavMultiplayer.setTextColor(pageIndex == 4 ? activeColor : inactiveColor);
        mTvNavSettings.setTextColor(pageIndex == 5 ? activeColor : inactiveColor);
    }

    @Override
    public void onClick(View v) {
        int id = v.getId();
        if (id == R.id.nav_tab_roleplay) {
            switchTab(0);
        } else if (id == R.id.nav_tab_characters) {
            switchTab(1);
        } else if (id == R.id.nav_tab_worlds) {
            switchTab(2);
        } else if (id == R.id.nav_tab_personas) {
            switchTab(3);
        } else if (id == R.id.nav_tab_multiplayer) {
            switchTab(4);
        } else if (id == R.id.nav_tab_settings) {
            switchTab(5);
        } else if (id == R.id.btn_send) {
            handleSendMessage();
        } else if (id == R.id.btn_quick_dice || id == R.id.btn_input_dice) {
            showDiceRollerDialog();
        } else if (id == R.id.chip_inspect) {
            prefixMessageInput("*Carefully inspects the surrounding area, observing details:* ");
        } else if (id == R.id.chip_whisper) {
            prefixMessageInput("*Leans in close and whispers softly:* ");
        } else if (id == R.id.chip_action) {
            prefixMessageInput("*Takes a decisive action:* ");
        } else if (id == R.id.chip_attack) {
            prefixMessageInput("*Lunges forward readying an attack!* ");
        } else if (id == R.id.chip_d20) {
            rollAndPostDice(20, 0);
        } else if (id == R.id.chip_clear) {
            handleClearChat();
        } else if (id == R.id.btn_new_character) {
            showCreateCharacterDialog();
        } else if (id == R.id.btn_new_persona) {
            showCreatePersonaDialog();
        } else if (id == R.id.btn_coop_scan) {
            startLanScan();
        } else if (id == R.id.btn_coop_connect) {
            handleCoopConnect();
        } else if (id == R.id.btn_lobby_change_character) {
            showLobbyChangeRoleDialog();
        } else if (id == R.id.btn_lobby_ready) {
            handleToggleLobbyReady();
        } else if (id == R.id.btn_lobby_enter_roleplay) {
            switchTab(0);
        } else if (id == R.id.btn_ai_test) {
            handleTestAiConnection();
        } else if (id == R.id.btn_ai_save) {
            handleSaveAiSettings();
        }
    }

    private void prefixMessageInput(String prefix) {
        String current = mEtMessageInput.getText().toString();
        mEtMessageInput.setText(prefix + current);
        mEtMessageInput.setSelection(mEtMessageInput.getText().length());
        mEtMessageInput.requestFocus();
    }

    private void handleSendMessage() {
        final String text = mEtMessageInput.getText().toString().trim();
        if (text.isEmpty() || mIsStreaming) return;

        mEtMessageInput.setText("");

        String senderName = mActivePersona != null ? mActivePersona.name : "Player";
        if (mIsMultiplayerConnected && mCurrentLobbyRole != null && !mCurrentLobbyRole.isEmpty()) {
            senderName = mCurrentLobbyRole;
        }

        ChatMessageModel userMsg = new ChatMessageModel(
                0, senderName, "user", text, System.currentTimeMillis(), "chat"
        );
        if (mActiveCharacter != null) {
            long id = mDb.saveMessage(mActiveCharacter.id, userMsg);
            userMsg.id = id;
        }
        mCurrentMessages.add(userMsg);
        mMessagesAdapter.notifyDataSetChanged();
        scrollChatToBottom();

        if (mIsMultiplayerConnected && mTcpClient != null) {
            mTcpClient.sendChatMessage(text);
        } else {
            triggerAiResponse(text);
        }
    }

    static class StreamListenerImpl implements AiChatEngine.StreamListener {
        private final MainActivity mActivity;

        StreamListenerImpl(MainActivity activity) {
            mActivity = activity;
        }

        @Override
        public void onChunk(String chunk) {
            if (mActivity.mStreamingMsg != null) {
                mActivity.mStreamingMsg.text += chunk;
                mActivity.mMessagesAdapter.notifyDataSetChanged();
                mActivity.scrollChatToBottom();
            }
        }

        @Override
        public void onComplete(String fullText) {
            mActivity.mIsStreaming = false;
            mActivity.mTvTypingStatus.setVisibility(View.GONE);
            if (mActivity.mStreamingMsg != null) {
                mActivity.mStreamingMsg.text = fullText;
                if (mActivity.mActiveCharacter != null) {
                    long id = mActivity.mDb.saveMessage(mActivity.mActiveCharacter.id, mActivity.mStreamingMsg);
                    mActivity.mStreamingMsg.id = id;
                }
                mActivity.mMessagesAdapter.notifyDataSetChanged();
                mActivity.scrollChatToBottom();
                mActivity.mStreamingMsg = null;
            }
        }

        @Override
        public void onError(String error) {
            mActivity.mIsStreaming = false;
            mActivity.mTvTypingStatus.setVisibility(View.GONE);
            if (mActivity.mStreamingMsg != null) {
                mActivity.mStreamingMsg.text += "\n[Error: " + error + "]";
                mActivity.mMessagesAdapter.notifyDataSetChanged();
                mActivity.mStreamingMsg = null;
            }
            Toast.makeText(mActivity, "AI Error: " + error, Toast.LENGTH_LONG).show();
        }
    }

    private void triggerAiResponse(String userPrompt) {
        if (mActiveCharacter == null) return;

        String endpoint = mDb.getConfig("endpoint", "https://openrouter.ai/api/v1");
        String apiKey = mDb.getConfig("api_key", "");
        String model = mDb.getConfig("model", "sao10k/l3-8b-stheno-v3.2");
        double temp = 0.82;
        int maxTokens = 700;
        try {
            temp = Double.parseDouble(mDb.getConfig("temperature", "0.82"));
            maxTokens = Integer.parseInt(mDb.getConfig("max_tokens", "700"));
        } catch (Exception ignored) { }

        mIsStreaming = true;
        mTvTypingStatus.setVisibility(View.VISIBLE);
        mTvTypingStatus.setText(mActiveCharacter.name + " is composing response...");

        mStreamingMsg = new ChatMessageModel(
                0, mActiveCharacter.name, "assistant", "", System.currentTimeMillis(), "chat"
        );
        mCurrentMessages.add(mStreamingMsg);
        mMessagesAdapter.notifyDataSetChanged();
        scrollChatToBottom();

        mAiEngine.streamCompletion(
                endpoint, apiKey, model, temp, maxTokens,
                mActiveCharacter, mActiveWorld, mActivePersona,
                mCurrentMessages, userPrompt,
                new StreamListenerImpl(this)
        );
    }

    // --- DICE ROLLER ---
    static class DiceSelectClickListener implements DialogInterface.OnClickListener {
        private final MainActivity mActivity;
        private final int[] mSides;
        DiceSelectClickListener(MainActivity activity, int[] sides) { mActivity = activity; mSides = sides; }
        @Override
        public void onClick(DialogInterface dialog, int which) {
            mActivity.rollAndPostDice(mSides[which], 0);
        }
    }

    private void showDiceRollerDialog() {
        final String[] diceOptions = { "d4", "d6", "d8", "d10", "d12", "d20", "d100" };
        final int[] sides = { 4, 6, 8, 10, 12, 20, 100 };

        AlertDialog.Builder builder = new AlertDialog.Builder(this);
        builder.setTitle("🎲 Select Tabletop Die");
        builder.setItems(diceOptions, new DiceSelectClickListener(this, sides));
        builder.setNegativeButton("Cancel", null);
        builder.show();
    }

    private void rollAndPostDice(int dieSides, int modifier) {
        DiceRoller.RollResult res = DiceRoller.roll(dieSides, modifier);

        try {
            Vibrator v = (Vibrator) getSystemService(Context.VIBRATOR_SERVICE);
            if (v != null) v.vibrate(60);
        } catch (Exception ignored) { }

        ChatMessageModel diceMsg = new ChatMessageModel(
                0, "System / Dice", "system", res.description, System.currentTimeMillis(), "dice"
        );
        if (mActiveCharacter != null) {
            long id = mDb.saveMessage(mActiveCharacter.id, diceMsg);
            diceMsg.id = id;
        }
        mCurrentMessages.add(diceMsg);
        mMessagesAdapter.notifyDataSetChanged();
        scrollChatToBottom();

        if (mIsMultiplayerConnected && mTcpClient != null) {
            mTcpClient.rollDice(dieSides, modifier);
        } else {
            triggerAiResponse("I rolled: " + res.description + ". Please react to this outcome in character.");
        }
    }

    static class ClearChatClickListener implements DialogInterface.OnClickListener {
        private final MainActivity mActivity;
        ClearChatClickListener(MainActivity activity) { mActivity = activity; }
        @Override
        public void onClick(DialogInterface dialog, int which) {
            if (mActivity.mActiveCharacter != null) {
                mActivity.mDb.clearMessagesForCharacter(mActivity.mActiveCharacter.id);
                mActivity.loadChatForActiveCharacter();
                Toast.makeText(mActivity, "Chat cleared.", Toast.LENGTH_SHORT).show();
            }
        }
    }

    private void handleClearChat() {
        if (mActiveCharacter == null) return;
        new AlertDialog.Builder(this)
                .setTitle("Clear Chat History?")
                .setMessage("Are you sure you want to clear conversation history with " + mActiveCharacter.name + "?")
                .setPositiveButton("Clear", new ClearChatClickListener(this))
                .setNegativeButton("Cancel", null)
                .show();
    }

    // --- CHARACTERS & WORLDS DIALOGS ---
    static class SaveCharacterClickListener implements DialogInterface.OnClickListener {
        private final MainActivity mActivity;
        private final EditText mName, mTitle, mDesc, mFirst;
        SaveCharacterClickListener(MainActivity activity, EditText name, EditText title, EditText desc, EditText first) {
            mActivity = activity; mName = name; mTitle = title; mDesc = desc; mFirst = first;
        }
        @Override
        public void onClick(DialogInterface dialog, int which) {
            String name = mName.getText().toString().trim();
            if (name.isEmpty()) return;

            CharacterModel c = new CharacterModel(
                    "char-custom-" + System.currentTimeMillis(),
                    name, mTitle.getText().toString().trim(), "Custom",
                    mDesc.getText().toString().trim(), mDesc.getText().toString().trim(),
                    "Natural, expressive", mFirst.getText().toString().trim(), "Custom"
            );
            mActivity.mDb.saveCharacter(c);
            mActivity.loadAllData();
            Toast.makeText(mActivity, "Character saved!", Toast.LENGTH_SHORT).show();
        }
    }

    private void showCreateCharacterDialog() {
        LinearLayout layout = new LinearLayout(this);
        layout.setOrientation(LinearLayout.VERTICAL);
        layout.setPadding(32, 16, 32, 16);

        final EditText etName = new EditText(this);
        etName.setHint("Character Name (e.g. Aki Hayakawa)");
        layout.addView(etName);

        final EditText etTitle = new EditText(this);
        etTitle.setHint("Title / Category (e.g. Public Safety Hunter)");
        layout.addView(etTitle);

        final EditText etDesc = new EditText(this);
        etDesc.setHint("Personality & Appearance");
        layout.addView(etDesc);

        final EditText etFirstMsg = new EditText(this);
        etFirstMsg.setHint("First Greeting Message");
        layout.addView(etFirstMsg);

        new AlertDialog.Builder(this)
                .setTitle("Create New Character")
                .setView(layout)
                .setPositiveButton("Save", new SaveCharacterClickListener(this, etName, etTitle, etDesc, etFirstMsg))
                .setNegativeButton("Cancel", null)
                .show();
    }

    static class SavePersonaClickListener implements DialogInterface.OnClickListener {
        private final MainActivity mActivity;
        private final EditText mName, mDesc;
        SavePersonaClickListener(MainActivity activity, EditText name, EditText desc) {
            mActivity = activity; mName = name; mDesc = desc;
        }
        @Override
        public void onClick(DialogInterface dialog, int which) {
            String name = mName.getText().toString().trim();
            if (name.isEmpty()) return;

            PersonaModel p = new PersonaModel(
                    "persona-custom-" + System.currentTimeMillis(),
                    name, mDesc.getText().toString().trim(),
                    mDesc.getText().toString().trim(), false
            );
            mActivity.mDb.savePersona(p);
            mActivity.loadAllData();
            Toast.makeText(mActivity, "Persona saved!", Toast.LENGTH_SHORT).show();
        }
    }

    private void showCreatePersonaDialog() {
        LinearLayout layout = new LinearLayout(this);
        layout.setOrientation(LinearLayout.VERTICAL);
        layout.setPadding(32, 16, 32, 16);

        final EditText etName = new EditText(this);
        etName.setHint("Persona Name (e.g. Detective / Partner)");
        layout.addView(etName);

        final EditText etDesc = new EditText(this);
        etDesc.setHint("Persona Traits & Speech Style");
        layout.addView(etDesc);

        new AlertDialog.Builder(this)
                .setTitle("Create Player Persona")
                .setView(layout)
                .setPositiveButton("Save", new SavePersonaClickListener(this, etName, etDesc))
                .setNegativeButton("Cancel", null)
                .show();
    }

    // --- CO-OP MULTIPLAYER ---
    private void setupMultiplayer() {
        mLanDiscovery = new LanDiscoveryListener(this);
        mTcpClient = new AetheriaTcpClient(this);
    }

    static class LanScanStopRunnable implements Runnable {
        private final Button mBtn;
        private final LanDiscoveryListener mListener;
        LanScanStopRunnable(Button btn, LanDiscoveryListener listener) { mBtn = btn; mListener = listener; }
        @Override
        public void run() {
            mBtn.setEnabled(true);
            mBtn.setText("📡 Scan LAN");
            mListener.stopListening();
        }
    }

    private void startLanScan() {
        mBtnCoopScan.setEnabled(false);
        mBtnCoopScan.setText("Scanning...");
        mLanDiscovery.startListening();
        Toast.makeText(this, "Scanning local Wi-Fi for PC host...", Toast.LENGTH_SHORT).show();
        mMainHandler.postDelayed(new LanScanStopRunnable(mBtnCoopScan, mLanDiscovery), 5000);
    }

    static class HostDiscoveredRunnable implements Runnable {
        private final MainActivity mActivity;
        private final String mIp;
        HostDiscoveredRunnable(MainActivity activity, String ip) { mActivity = activity; mIp = ip; }
        @Override
        public void run() {
            mActivity.mEtCoopHost.setText(mIp);
            mActivity.mEtCoopPort.setText("7777");
            Toast.makeText(mActivity, "Found host: " + mIp, Toast.LENGTH_LONG).show();
        }
    }

    @Override
    public void onHostDiscovered(final String hostIp) {
        mMainHandler.post(new HostDiscoveredRunnable(this, hostIp));
    }

    private void handleCoopConnect() {
        if (mIsMultiplayerConnected) {
            if (mTcpClient != null) mTcpClient.disconnect();
            mIsMultiplayerConnected = false;
            mTvCoopStatusDot.setBackgroundColor(Color.parseColor("#9CA3AF"));
            mTvCoopStatusText.setText("Disconnected (Solo Mode)");
            mTvCoopDetails.setText("In Solo Mode, characters respond using your in-app AI engine.");
            mBtnCoopConnect.setText("Connect");
            if (mLlLobbySection != null) mLlLobbySection.setVisibility(View.GONE);
            updateTopBar();
            return;
        }

        String host = mEtCoopHost.getText().toString().trim();
        String portStr = mEtCoopPort.getText().toString().trim();
        String token = mEtCoopToken.getText().toString().trim();
        int port = 7777;
        try { port = Integer.parseInt(portStr); } catch (Exception ignored) { }

        if (host.isEmpty()) {
            Toast.makeText(this, "Please enter Host IP Address", Toast.LENGTH_SHORT).show();
            return;
        }

        String personaName = mCurrentLobbyRole != null ? mCurrentLobbyRole : (mActivePersona != null ? mActivePersona.name : "Player");
        mBtnCoopConnect.setEnabled(false);
        mBtnCoopConnect.setText("Connecting...");

        mTcpClient.connect(host, port, token, personaName);
    }

    static class ConnectedRunnable implements Runnable {
        private final MainActivity mActivity;
        private final String mParticipantId;
        ConnectedRunnable(MainActivity activity, String participantId) {
            mActivity = activity;
            mParticipantId = participantId;
        }
        @Override
        public void run() {
            mActivity.mIsMultiplayerConnected = true;
            mActivity.mBtnCoopConnect.setEnabled(true);
            mActivity.mBtnCoopConnect.setText("Disconnect");
            mActivity.mTvCoopStatusDot.setBackgroundColor(Color.parseColor("#10B981"));
            mActivity.mTvCoopStatusText.setText("Connected to PC Host!");
            mActivity.mTvCoopDetails.setText("Lobby synchronizing... Party room is active.");
            if (mActivity.mLlLobbySection != null) {
                mActivity.mLlLobbySection.setVisibility(View.VISIBLE);
            }
            mActivity.updateLobbyRoleDisplay();
            mActivity.updateTopBar();
            Toast.makeText(mActivity, "Connected to PC Host!", Toast.LENGTH_SHORT).show();
        }
    }

    @Override
    public void onConnected(String participantId) {
        mMainHandler.post(new ConnectedRunnable(this, participantId));
    }

    private void updateLobbyRoleDisplay() {
        if (mTvLobbySelectedRole != null) {
            mTvLobbySelectedRole.setText("Playing as: " + mCurrentLobbyRole);
        }
        if (mTvLobbyCharPreview != null) {
            CharacterModel charModel = findCharacterByName(mCurrentLobbyRole);
            if (charModel != null && charModel.description != null && !charModel.description.isEmpty()) {
                mTvLobbyCharPreview.setText(charModel.description);
            } else {
                mTvLobbyCharPreview.setText("Custom persona identity chosen for party roleplay.");
            }
        }
    }

    private CharacterModel findCharacterByName(String name) {
        for (CharacterModel c : mAllCharacters) {
            if (c.name.equalsIgnoreCase(name)) return c;
        }
        return null;
    }

    static class RoleSelectedListener implements DialogInterface.OnClickListener {
        private final MainActivity mActivity;
        private final String[] mRoles;
        RoleSelectedListener(MainActivity activity, String[] roles) {
            mActivity = activity;
            mRoles = roles;
        }
        @Override
        public void onClick(DialogInterface dialog, int which) {
            mActivity.selectLobbyRole(mRoles[which]);
        }
    }

    private void showLobbyChangeRoleDialog() {
        List<String> roleOptions = new ArrayList<>();
        for (CharacterModel c : mAllCharacters) {
            roleOptions.add(c.name);
        }
        for (PersonaModel p : mAllPersonas) {
            if (!roleOptions.contains(p.name)) {
                roleOptions.add(p.name);
            }
        }
        if (!roleOptions.contains("Reze")) roleOptions.add(0, "Reze");
        if (!roleOptions.contains("Denji")) roleOptions.add(1, "Denji");

        final String[] rolesArray = roleOptions.toArray(new String[0]);
        AlertDialog.Builder builder = new AlertDialog.Builder(this);
        builder.setTitle("🎭 Pick Your Roleplay Character");
        builder.setItems(rolesArray, new RoleSelectedListener(this, rolesArray));
        builder.setNegativeButton("Cancel", null);
        builder.show();
    }

    private void selectLobbyRole(String roleName) {
        mCurrentLobbyRole = roleName;
        updateLobbyRoleDisplay();

        CharacterModel c = findCharacterByName(roleName);
        String charId = c != null ? c.id : "";
        if (c != null) {
            mActiveCharacter = c;
            updateTopBar();
        }

        for (PersonaModel p : mAllPersonas) {
            if (p.name.equalsIgnoreCase(roleName)) {
                mActivePersona = p;
                break;
            }
        }
        if (mActivePersona == null || !mActivePersona.name.equalsIgnoreCase(roleName)) {
            mActivePersona = new PersonaModel("persona-" + roleName.toLowerCase(), roleName, roleName, roleName, false);
        }

        if (mIsMultiplayerConnected && mTcpClient != null) {
            mTcpClient.updateIdentity(roleName, charId, mIsLobbyReady);
        }
        Toast.makeText(this, "Selected Character: " + roleName, Toast.LENGTH_SHORT).show();
    }

    private void handleToggleLobbyReady() {
        mIsLobbyReady = !mIsLobbyReady;
        if (mBtnLobbyReady != null) {
            mBtnLobbyReady.setText(mIsLobbyReady ? "✅ Ready!" : "⌛ Not Ready");
            mBtnLobbyReady.setTextColor(mIsLobbyReady ? Color.parseColor("#10B981") : Color.parseColor("#E6E1E5"));
        }

        if (mIsMultiplayerConnected && mTcpClient != null) {
            CharacterModel c = findCharacterByName(mCurrentLobbyRole);
            String charId = c != null ? c.id : "";
            mTcpClient.updateIdentity(mCurrentLobbyRole, charId, mIsLobbyReady);
        }
    }

    static class LobbyStateUpdateRunnable implements Runnable {
        private final MainActivity mActivity;
        private final String mRoomTitle, mGameMode, mWorldName, mParticipantsJson;
        private final boolean mGameStarted;

        LobbyStateUpdateRunnable(MainActivity a, String title, String mode, String world, boolean started, String parts) {
            mActivity = a;
            mRoomTitle = title;
            mGameMode = mode;
            mWorldName = world;
            mGameStarted = started;
            mParticipantsJson = parts;
        }

        @Override
        public void run() {
            if (mActivity.mLlLobbySection != null) {
                mActivity.mLlLobbySection.setVisibility(View.VISIBLE);
            }
            if (mActivity.mTvLobbyRoomTitle != null) {
                mActivity.mTvLobbyRoomTitle.setText(mRoomTitle);
            }
            if (mActivity.mTvLobbyMeta != null) {
                mActivity.mTvLobbyMeta.setText("Mode: " + mGameMode + " • World: " + mWorldName);
            }
            mActivity.renderPartyRoster(mParticipantsJson);
            if (mGameStarted) {
                mActivity.switchTab(0);
                Toast.makeText(mActivity, "⚔️ Adventure Launched by Host!", Toast.LENGTH_SHORT).show();
            }
        }
    }

    @Override
    public void onLobbyStateUpdated(String roomTitle, String gameMode, String worldName, boolean gameStarted, String participantsJson) {
        mMainHandler.post(new LobbyStateUpdateRunnable(this, roomTitle, gameMode, worldName, gameStarted, participantsJson));
    }

    static class GameLaunchRunnable implements Runnable {
        private final MainActivity mActivity;
        GameLaunchRunnable(MainActivity a) { mActivity = a; }
        @Override
        public void run() {
            mActivity.switchTab(0);
            Toast.makeText(mActivity, "🚀 Entering Roleplay Workspace!", Toast.LENGTH_SHORT).show();
        }
    }

    @Override
    public void onGameLaunchReceived() {
        mMainHandler.post(new GameLaunchRunnable(this));
    }

    static class ParticipantsUpdateRunnable implements Runnable {
        private final MainActivity mActivity;
        private final String mPartsJson;
        ParticipantsUpdateRunnable(MainActivity a, String partsJson) { mActivity = a; mPartsJson = partsJson; }
        @Override
        public void run() {
            mActivity.renderPartyRoster(mPartsJson);
        }
    }

    @Override
    public void onParticipantsUpdated(String participantsJson) {
        mMainHandler.post(new ParticipantsUpdateRunnable(this, participantsJson));
    }

    private void renderPartyRoster(String jsonStr) {
        if (mLlLobbyPartyMembers == null) return;
        mLlLobbyPartyMembers.removeAllViews();
        try {
            JSONArray arr = new JSONArray(jsonStr);
            List<String> aiCompanions = new ArrayList<>();
            for (int i = 0; i < arr.length(); i++) {
                JSONObject p = arr.getJSONObject(i);
                boolean isAi = p.optBoolean("IsAiCharacter", false);
                if (isAi) {
                    aiCompanions.add(p.optString("DisplayName", "AI Character"));
                    continue;
                }
                String name = p.optString("DisplayName", "Member");
                String persona = p.optString("PersonaName", "");
                boolean isHost = p.optBoolean("IsHost", false);
                boolean isReady = p.optBoolean("IsReady", false);

                LinearLayout row = new LinearLayout(this);
                row.setOrientation(LinearLayout.HORIZONTAL);
                row.setPadding(12, 10, 12, 10);
                row.setBackgroundColor(Color.parseColor("#26242C"));

                LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                        LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT
                );
                lp.setMargins(0, 0, 0, 6);
                row.setLayoutParams(lp);

                TextView tvName = new TextView(this);
                String label = (isHost ? "👑 " : "👤 ") + name;
                if (!persona.isEmpty() && !persona.equalsIgnoreCase(name)) {
                    label += " (" + persona + ")";
                }
                tvName.setText(label);
                tvName.setTextColor(Color.parseColor("#E6E1E5"));
                tvName.setTextSize(13);
                tvName.setTypeface(null, android.graphics.Typeface.BOLD);
                LinearLayout.LayoutParams nameLp = new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1.0f);
                tvName.setLayoutParams(nameLp);
                row.addView(tvName);

                TextView tvReady = new TextView(this);
                tvReady.setText((isHost || isReady) ? "READY" : "WAITING");
                tvReady.setTextColor((isHost || isReady) ? Color.parseColor("#10B981") : Color.parseColor("#F59E0B"));
                tvReady.setTextSize(11);
                tvReady.setTypeface(null, android.graphics.Typeface.BOLD);
                tvReady.setPadding(8, 2, 8, 2);
                row.addView(tvReady);

                mLlLobbyPartyMembers.addView(row);
            }

            if (!aiCompanions.isEmpty()) {
                TextView tvAi = new TextView(this);
                StringBuilder sb = new StringBuilder("🤖 AI Companions in Scenario: ");
                for (int j = 0; j < aiCompanions.size(); j++) {
                    if (j > 0) sb.append(", ");
                    sb.append(aiCompanions.get(j));
                }
                tvAi.setText(sb.toString());
                tvAi.setTextColor(Color.parseColor("#A78BFA"));
                tvAi.setTextSize(11);
                tvAi.setPadding(8, 6, 8, 4);
                mLlLobbyPartyMembers.addView(tvAi);
            }
        } catch (Exception ignored) { }
    }

    static class ChatMsgReceivedRunnable implements Runnable {
        private final MainActivity mActivity;
        private final String mSender, mText;
        private final long mTs;
        ChatMsgReceivedRunnable(MainActivity a, String s, String t, long ts) {
            mActivity = a; mSender = s; mText = t; mTs = ts;
        }
        @Override
        public void run() {
            ChatMessageModel msg = new ChatMessageModel(
                    0, mSender, "assistant", mText, mTs, "chat"
            );
            mActivity.mCurrentMessages.add(msg);
            mActivity.mMessagesAdapter.notifyDataSetChanged();
            mActivity.scrollChatToBottom();
        }
    }

    @Override
    public void onChatMessageReceived(String sender, String text, long timestamp, String actionType) {
        mMainHandler.post(new ChatMsgReceivedRunnable(this, sender, text, timestamp));
    }

    static class StreamChunkRunnable implements Runnable {
        private final MainActivity mActivity;
        private final String mSender, mDelta;
        StreamChunkRunnable(MainActivity a, String s, String d) { mActivity = a; mSender = s; mDelta = d; }
        @Override
        public void run() {
            if (mActivity.mStreamingMsg != null) {
                mActivity.mStreamingMsg.text += mDelta;
                mActivity.mMessagesAdapter.notifyDataSetChanged();
                mActivity.scrollChatToBottom();
            } else {
                mActivity.mStreamingMsg = new ChatMessageModel(
                        0, mSender, "assistant", mDelta, System.currentTimeMillis(), "chat"
                );
                mActivity.mCurrentMessages.add(mActivity.mStreamingMsg);
                mActivity.mMessagesAdapter.notifyDataSetChanged();
                mActivity.scrollChatToBottom();
            }
        }
    }

    @Override
    public void onAiStreamChunk(String sender, String delta) {
        mMainHandler.post(new StreamChunkRunnable(this, sender, delta));
    }

    @Override
    public void onCharacterSynced(String charJson) {
        // Character synchronized from host
    }

    static class DiceRolledRunnable implements Runnable {
        private final MainActivity mActivity;
        private final String mRoller, mDesc;
        DiceRolledRunnable(MainActivity a, String r, String d) {
            mActivity = a; mRoller = r; mDesc = d;
        }
        @Override
        public void run() {
            ChatMessageModel msg = new ChatMessageModel(
                    0, mRoller, "system", "🎲 " + mRoller + " rolled: " + mDesc, System.currentTimeMillis(), "dice"
            );
            mActivity.mCurrentMessages.add(msg);
            mActivity.mMessagesAdapter.notifyDataSetChanged();
            mActivity.scrollChatToBottom();
        }
    }

    @Override
    public void onDiceRolled(String roller, int sides, int total, String description) {
        mMainHandler.post(new DiceRolledRunnable(this, roller, description));
    }

    static class DisconnectedRunnable implements Runnable {
        private final MainActivity mActivity;
        private final String mReason;
        DisconnectedRunnable(MainActivity a, String r) { mActivity = a; mReason = r; }
        @Override
        public void run() {
            mActivity.mIsMultiplayerConnected = false;
            mActivity.mBtnCoopConnect.setEnabled(true);
            mActivity.mBtnCoopConnect.setText("Connect");
            mActivity.mTvCoopStatusDot.setBackgroundColor(Color.parseColor("#EF4444"));
            mActivity.mTvCoopStatusText.setText("Disconnected: " + mReason);
            if (mActivity.mLlLobbySection != null) {
                mActivity.mLlLobbySection.setVisibility(View.GONE);
            }
            mActivity.updateTopBar();
        }
    }

    @Override
    public void onDisconnected(String reason) {
        mMainHandler.post(new DisconnectedRunnable(this, reason));
    }

    static class ErrorRunnable implements Runnable {
        private final MainActivity mActivity;
        private final String mError;
        ErrorRunnable(MainActivity a, String e) { mActivity = a; mError = e; }
        @Override
        public void run() {
            mActivity.mBtnCoopConnect.setEnabled(true);
            mActivity.mBtnCoopConnect.setText("Connect");
            Toast.makeText(mActivity, "Network Error: " + mError, Toast.LENGTH_LONG).show();
        }
    }

    @Override
    public void onError(String error) {
        mMainHandler.post(new ErrorRunnable(this, error));
    }

    // --- AI SETTINGS ---
    static class TestListenerImpl implements AiChatEngine.TestListener {
        private final MainActivity mActivity;
        TestListenerImpl(MainActivity a) { mActivity = a; }
        @Override
        public void onResult(boolean success, String message) {
            mActivity.mBtnAiTest.setEnabled(true);
            mActivity.mBtnAiTest.setText("⚡ Test Connection");
            mActivity.mTvAiTestResult.setVisibility(View.VISIBLE);
            mActivity.mTvAiTestResult.setText(message);
            mActivity.mTvAiTestResult.setTextColor(success ? Color.parseColor("#10B981") : Color.parseColor("#EF4444"));
        }
    }

    private void handleTestAiConnection() {
        String endpoint = mEtAiEndpoint.getText().toString().trim();
        String apiKey = mEtAiKey.getText().toString().trim();
        String model = mEtAiModel.getText().toString().trim();

        mBtnAiTest.setEnabled(false);
        mBtnAiTest.setText("Testing...");
        mTvAiTestResult.setVisibility(View.GONE);

        mAiEngine.testConnection(endpoint, apiKey, model, new TestListenerImpl(this));
    }

    private void handleSaveAiSettings() {
        mDb.setConfig("endpoint", mEtAiEndpoint.getText().toString().trim());
        mDb.setConfig("api_key", mEtAiKey.getText().toString().trim());
        mDb.setConfig("model", mEtAiModel.getText().toString().trim());
        mDb.setConfig("temperature", mEtAiTemp.getText().toString().trim());
        mDb.setConfig("max_tokens", mEtAiMaxTokens.getText().toString().trim());
        Toast.makeText(this, "AI Settings saved!", Toast.LENGTH_SHORT).show();
    }

    @Override
    protected void onDestroy() {
        super.onDestroy();
        if (mLanDiscovery != null) mLanDiscovery.stopListening();
        if (mTcpClient != null) mTcpClient.disconnect();
    }

    // ================= ADAPTERS =================
    static class MessagesAdapter extends BaseAdapter {
        private final Context mContext;
        private final List<ChatMessageModel> mList;
        private final SimpleDateFormat mSdf = new SimpleDateFormat("HH:mm", Locale.getDefault());

        MessagesAdapter(Context ctx, List<ChatMessageModel> list) {
            mContext = ctx;
            mList = list;
        }

        @Override public int getCount() { return mList.size(); }
        @Override public Object getItem(int position) { return mList.get(position); }
        @Override public long getItemId(int position) { return position; }

        @Override
        public View getView(int position, View convertView, ViewGroup parent) {
            if (convertView == null) {
                convertView = LayoutInflater.from(mContext).inflate(R.layout.item_chat_message, parent, false);
            }

            ChatMessageModel msg = mList.get(position);
            TextView tvAvatar = convertView.findViewById(R.id.tv_msg_avatar);
            TextView tvSender = convertView.findViewById(R.id.tv_msg_sender);
            TextView tvTime = convertView.findViewById(R.id.tv_msg_time);
            TextView tvText = convertView.findViewById(R.id.tv_msg_text);
            View bubble = convertView.findViewById(R.id.ll_bubble_container);

            tvSender.setText(msg.sender);
            tvTime.setText(mSdf.format(new Date(msg.timestamp)));
            tvText.setText(msg.text);
            tvAvatar.setText(!msg.sender.isEmpty() ? msg.sender.substring(0, 1).toUpperCase() : "?");

            if ("user".equals(msg.role)) {
                bubble.setBackgroundResource(R.drawable.bubble_user_bg);
                tvText.setTextColor(mContext.getResources().getColor(R.color.bubble_user_text));
            } else if ("system".equals(msg.role) || "dice".equals(msg.actionType)) {
                bubble.setBackgroundResource(R.drawable.bubble_system_bg);
                tvText.setTextColor(mContext.getResources().getColor(R.color.bubble_system_text));
            } else {
                bubble.setBackgroundResource(R.drawable.bubble_char_bg);
                tvText.setTextColor(mContext.getResources().getColor(R.color.bubble_char_text));
            }

            return convertView;
        }
    }

    static class CharChatClickListener implements View.OnClickListener {
        private final MainActivity mActivity;
        private final CharacterModel mChar;
        CharChatClickListener(MainActivity activity, CharacterModel c) { mActivity = activity; mChar = c; }
        @Override
        public void onClick(View v) {
            mActivity.mActiveCharacter = mChar;
            mActivity.updateTopBar();
            mActivity.loadChatForActiveCharacter();
            mActivity.switchTab(0);
        }
    }

    static class CharactersAdapter extends BaseAdapter {
        private final MainActivity mActivity;
        private final List<CharacterModel> mList;

        CharactersAdapter(MainActivity activity, List<CharacterModel> list) {
            mActivity = activity;
            mList = list;
        }

        @Override public int getCount() { return mList.size(); }
        @Override public Object getItem(int position) { return mList.get(position); }
        @Override public long getItemId(int position) { return position; }

        @Override
        public View getView(int position, View convertView, ViewGroup parent) {
            if (convertView == null) {
                convertView = LayoutInflater.from(mActivity).inflate(R.layout.item_character_card, parent, false);
            }
            CharacterModel c = mList.get(position);
            TextView tvAvatar = convertView.findViewById(R.id.tv_char_card_avatar);
            TextView tvName = convertView.findViewById(R.id.tv_char_card_name);
            TextView tvTitle = convertView.findViewById(R.id.tv_char_card_title);
            TextView tvDesc = convertView.findViewById(R.id.tv_char_card_desc);
            TextView tvTags = convertView.findViewById(R.id.tv_char_card_tags);
            Button btnChat = convertView.findViewById(R.id.btn_char_card_chat);

            tvName.setText(c.name);
            tvTitle.setText(c.title);
            tvDesc.setText(c.description);
            tvTags.setText(c.tags);
            tvAvatar.setText(!c.name.isEmpty() ? c.name.substring(0, 1).toUpperCase() : "C");

            btnChat.setOnClickListener(new CharChatClickListener(mActivity, c));
            return convertView;
        }
    }

    static class WorldSelectClickListener implements View.OnClickListener {
        private final MainActivity mActivity;
        private final WorldModel mWorld;
        WorldSelectClickListener(MainActivity activity, WorldModel w) { mActivity = activity; mWorld = w; }
        @Override
        public void onClick(View v) {
            mActivity.mActiveWorld = mWorld;
            mActivity.updateTopBar();
            Toast.makeText(mActivity, "Active World: " + mWorld.name, Toast.LENGTH_SHORT).show();
            mActivity.switchTab(0);
        }
    }

    static class WorldsAdapter extends BaseAdapter {
        private final MainActivity mActivity;
        private final List<WorldModel> mList;

        WorldsAdapter(MainActivity activity, List<WorldModel> list) {
            mActivity = activity;
            mList = list;
        }

        @Override public int getCount() { return mList.size(); }
        @Override public Object getItem(int position) { return mList.get(position); }
        @Override public long getItemId(int position) { return position; }

        @Override
        public View getView(int position, View convertView, ViewGroup parent) {
            if (convertView == null) {
                convertView = LayoutInflater.from(mActivity).inflate(R.layout.item_world_card, parent, false);
            }
            WorldModel w = mList.get(position);
            TextView tvName = convertView.findViewById(R.id.tv_world_card_name);
            TextView tvGenre = convertView.findViewById(R.id.tv_world_card_genre);
            TextView tvSetting = convertView.findViewById(R.id.tv_world_card_setting);
            Button btnSelect = convertView.findViewById(R.id.btn_world_card_select);

            tvName.setText(w.name);
            tvGenre.setText(w.genre + " • " + w.tone);
            tvSetting.setText(w.setting);

            btnSelect.setOnClickListener(new WorldSelectClickListener(mActivity, w));
            return convertView;
        }
    }

    static class PersonaSelectClickListener implements View.OnClickListener {
        private final MainActivity mActivity;
        private final PersonaModel mPersona;
        private final PersonasAdapter mAdapter;
        PersonaSelectClickListener(MainActivity a, PersonaModel p, PersonasAdapter ad) {
            mActivity = a; mPersona = p; mAdapter = ad;
        }
        @Override
        public void onClick(View v) {
            mActivity.mActivePersona = mPersona;
            mAdapter.notifyDataSetChanged();
            Toast.makeText(mActivity, "Active Persona: " + mPersona.name, Toast.LENGTH_SHORT).show();
        }
    }

    static class PersonasAdapter extends BaseAdapter {
        private final MainActivity mActivity;
        private final List<PersonaModel> mList;

        PersonasAdapter(MainActivity activity, List<PersonaModel> list) {
            mActivity = activity;
            mList = list;
        }

        @Override public int getCount() { return mList.size(); }
        @Override public Object getItem(int position) { return mList.get(position); }
        @Override public long getItemId(int position) { return position; }

        @Override
        public View getView(int position, View convertView, ViewGroup parent) {
            if (convertView == null) {
                convertView = LayoutInflater.from(mActivity).inflate(R.layout.item_persona_card, parent, false);
            }
            PersonaModel p = mList.get(position);
            TextView tvName = convertView.findViewById(R.id.tv_persona_card_name);
            TextView tvDesc = convertView.findViewById(R.id.tv_persona_card_desc);
            TextView tvBadge = convertView.findViewById(R.id.tv_persona_badge);

            tvName.setText(p.name);
            tvDesc.setText(p.description);

            boolean isActive = (mActivity.mActivePersona != null && mActivity.mActivePersona.id.equals(p.id));
            tvBadge.setVisibility(isActive ? View.VISIBLE : View.GONE);

            convertView.setOnClickListener(new PersonaSelectClickListener(mActivity, p, this));
            return convertView;
        }
    }
}
