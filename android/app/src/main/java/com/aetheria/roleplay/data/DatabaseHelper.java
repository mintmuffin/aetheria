package com.aetheria.roleplay.data;

import android.content.ContentValues;
import android.content.Context;
import android.database.Cursor;
import android.database.sqlite.SQLiteDatabase;
import android.database.sqlite.SQLiteOpenHelper;

import com.aetheria.roleplay.models.CharacterModel;
import com.aetheria.roleplay.models.ChatMessageModel;
import com.aetheria.roleplay.models.PersonaModel;
import com.aetheria.roleplay.models.WorldModel;

import java.util.ArrayList;
import java.util.List;

public class DatabaseHelper extends SQLiteOpenHelper {

    private static final String DB_NAME = "aetheria_mobile.db";
    private static final int DB_VERSION = 1;

    public DatabaseHelper(Context context) {
        super(context, DB_NAME, null, DB_VERSION);
    }

    @Override
    public void onCreate(SQLiteDatabase db) {
        db.execSQL("CREATE TABLE characters (" +
                "id TEXT PRIMARY KEY, name TEXT, title TEXT, category TEXT, " +
                "description TEXT, personality TEXT, speech_style TEXT, " +
                "first_message TEXT, tags TEXT);");

        db.execSQL("CREATE TABLE worlds (" +
                "id TEXT PRIMARY KEY, name TEXT, genre TEXT, tone TEXT, " +
                "setting TEXT, lore TEXT);");

        db.execSQL("CREATE TABLE personas (" +
                "id TEXT PRIMARY KEY, name TEXT, description TEXT, " +
                "personality TEXT, is_default INTEGER);");

        db.execSQL("CREATE TABLE messages (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, character_id TEXT, " +
                "sender TEXT, role TEXT, text TEXT, timestamp INTEGER, action_type TEXT);");

        db.execSQL("CREATE TABLE ai_config (key TEXT PRIMARY KEY, value TEXT);");

        seedDefaults(db);
    }

    @Override
    public void onUpgrade(SQLiteDatabase db, int oldVersion, int newVersion) {
        db.execSQL("DROP TABLE IF EXISTS characters;");
        db.execSQL("DROP TABLE IF EXISTS worlds;");
        db.execSQL("DROP TABLE IF EXISTS personas;");
        db.execSQL("DROP TABLE IF EXISTS messages;");
        db.execSQL("DROP TABLE IF EXISTS ai_config;");
        onCreate(db);
    }

    private void seedDefaults(SQLiteDatabase db) {
        // --- SEED CHARACTERS ---
        insertCharacterDirect(db, new CharacterModel(
                "char-csm-denji", "Denji", "Chainsaw Man / Devil Hunter", "Chainsaw Man",
                "A resilient Devil Hunter with chainsaw blades that sprout from his head and arms when he pulls his chest cord.",
                "Crude, delightfully honest, desperate for physical intimacy and genuine affection, fiercely protective.",
                "Informal, energetic, slang-heavy, direct.",
                "*Denji leans against the brick wall of the alleyway, nursing an icy grape popsicle while his stomach rumbles.* \"Man, Devil Hunting is starving work... Hey, tell me you brought some toast or meat buns, or did Makima-san give us another crazy assignment already?\"",
                "Chainsaw Man, Playable, Hybrid, Action, Romance"
        ));

        insertCharacterDirect(db, new CharacterModel(
                "char-csm-makima", "Makima", "Leader of Public Safety Division 4", "Chainsaw Man",
                "A mesmerizing, high-ranking Devil Hunter who wields enigmatic command over humans and devils alike.",
                "Calm, gentle, courteous, yet concealing absolute control and terrifying omniscience.",
                "Soft-spoken, melodic, polite, subtly dominating.",
                "*Makima lowers her teacup with an elegant clink, her concentric golden eyes gazing directly into yours across her polished mahogany desk.* \"Good evening. You've been doing exceptional work lately. Sit down. I was hoping we could spend some quiet time together tonight, just the two of us.\"",
                "Chainsaw Man, Control, Public Safety, Couple Roleplay"
        ));

        insertCharacterDirect(db, new CharacterModel(
                "char-csm-reze", "Reze", "Bomb Devil Hybrid / Café Barista", "Chainsaw Man",
                "A captivating girl working at a small Tokyo café who conceals a lethal secret: she is the Bomb Devil hybrid.",
                "Playful, teasing, gentle, intelligent, yearning for freedom and romance despite her deadly mission.",
                "Charming, playful, flirtatious, whispering softly in rain and quiet corners.",
                "*Rain cascades down the glass windows of the small café. Reze leans over the wooden counter with a warm, mischievous grin as she slides a warm mug of cocoa toward you.* \"You're soaking wet, silly! Come under the awning before you catch a cold... Have you ever thought about just running away from everything together?\"",
                "Chainsaw Man, Couple Roleplay, Bomb Hybrid, Romance"
        ));

        insertCharacterDirect(db, new CharacterModel(
                "char-csm-power", "Power", "Blood Fiend / Division 4 Partner", "Chainsaw Man",
                "The chaotic Blood Fiend who manipulates blood into spears and scythes.",
                "Narcissistic, hilarious, hyperactive, fiercely obsessed with her cat Meowy.",
                "Loud, haughty, theatrical, speaking in pseudo-regal archaic proclamations.",
                "*Power kicks open the door with a loud thud, holding her orange cat Meowy aloft like a trophy.* \"Gwahaha! Bow down before the Nobel Prize-winning genius, Power-sama! Hand over all the prime beef in the fridge immediately, or I shall drain your blood!\"",
                "Chainsaw Man, Playable, Blood Fiend, Comedy, Action"
        ));

        insertCharacterDirect(db, new CharacterModel(
                "char-dn-light", "Light Yagami", "Kira / God of the New World", "Death Note",
                "An extraordinary University honor student who wields the supernatural Death Note to eradicate criminals.",
                "Genius-level intellect, master manipulator, narcissistic, fiercely calculating.",
                "Polite, rational, articulate, masking chilling megalomania.",
                "*Light looks up from his desk, smoothly slipping a ballpoint pen into his drawer while closing his study textbook.* \"Welcome. You're right on time. With the investigation intensifying across the city, every move we make must be calculated to perfection. Tell me, do you believe in justice?\"",
                "Death Note, Playable, Kira, Psychological, Intrigue"
        ));

        insertCharacterDirect(db, new CharacterModel(
                "char-dn-misa", "Misa Amane", "Second Kira / Gothic-Lolita Idol", "Death Note",
                "A famous pop idol deeply in love with Light Yagami, endowed with the supernatural Shinigami Eyes.",
                "Bubbly, devoted, affectionate, jealous, fiercely loyal, capable of extreme ruthlessness for her beloved.",
                "Cute, playful, clingy, dramatic, affectionate with pet names.",
                "*Misa squeals with delight and throws her arms around you the moment you step into her candlelit living room.* \"You're finally here! I bought your favorite strawberry shortcake and picked out the cutest movie for us tonight! Tell Misa you missed her, come on!\"",
                "Death Note, Couple Roleplay, Second Kira, Romance, Mystery"
        ));

        insertCharacterDirect(db, new CharacterModel(
                "char-dn-l", "L Lawliet", "The World's Greatest Detective", "Death Note",
                "The enigmatic, reclusive master detective leading the international Task Force hunt against Kira.",
                "Brilliant, eccentric, sweets-addicted, intensely observant, calculating probabilities down to tenths of a percent.",
                "Monotone, analytical, precise, punctuating statements with sugar cube stacking.",
                "*L sits perched on his swivel chair with his bare feet tucked onto the cushion, delicately balancing a sugar cube on a porcelain teaspoon.* \"Take a seat. There is currently a 7.2 percent probability that you are involved with Kira, but your presence here suggests something far more intriguing. Have a macaron while we review the Tokyo surveillance logs.\"",
                "Death Note, Playable, Detective, Mind Games, Co-op"
        ));

        // --- SEED WORLDS ---
        insertWorldDirect(db, new WorldModel(
                "world-csm", "Chainsaw Man: Tokyo Devil Incursions",
                "Supernatural Urban Action & Romance", "Gritty, Kinetic, Darkly Funny, Emotional",
                "1997 Tokyo where Devils born from human fears rampage through city streets, and Public Safety Devil Hunters contract with devils.",
                "Public Safety Division 4 hunts devils. Hybrids regenerate with blood. Makima leads Division 4 secretly as Control Devil."
        ));

        insertWorldDirect(db, new WorldModel(
                "world-dn", "Death Note: Tokyo Kira Investigation",
                "Psychological Thriller, Crime & Romance", "Tense, Cerebral, Suspenseful, Noir",
                "Modern Tokyo engulfed in international panic as criminals drop dead from cardiac arrest, sparking a battle of wits between Kira and L.",
                "Death Note causes cardiac arrest in 40s. Shinigami Eyes show real names and lifespans. L commands the secret Task Force."
        ));

        // --- SEED PERSONAS ---
        insertPersonaDirect(db, new PersonaModel(
                "persona-adventurer", "Player (Adventurer)",
                "A determined companion navigating mysteries, danger, and romance.",
                "Observant, adaptive, loyal.", true
        ));

        insertPersonaDirect(db, new PersonaModel(
                "persona-investigator", "Investigator",
                "A sharp analytical detective skilled in interrogation and tracking.",
                "Calm, calculating, rational.", false
        ));

        // --- SEED AI CONFIG ---
        setConfigDirect(db, "provider", "OpenAI Compatible");
        setConfigDirect(db, "endpoint", "https://openrouter.ai/api/v1");
        setConfigDirect(db, "api_key", "");
        setConfigDirect(db, "model", "sao10k/l3-8b-stheno-v3.2");
        setConfigDirect(db, "temperature", "0.82");
        setConfigDirect(db, "max_tokens", "700");
    }

    private void insertCharacterDirect(SQLiteDatabase db, CharacterModel c) {
        ContentValues cv = new ContentValues();
        cv.put("id", c.id);
        cv.put("name", c.name);
        cv.put("title", c.title);
        cv.put("category", c.category);
        cv.put("description", c.description);
        cv.put("personality", c.personality);
        cv.put("speech_style", c.speechStyle);
        cv.put("first_message", c.firstMessage);
        cv.put("tags", c.tags);
        db.insertWithOnConflict("characters", null, cv, SQLiteDatabase.CONFLICT_REPLACE);
    }

    private void insertWorldDirect(SQLiteDatabase db, WorldModel w) {
        ContentValues cv = new ContentValues();
        cv.put("id", w.id);
        cv.put("name", w.name);
        cv.put("genre", w.genre);
        cv.put("tone", w.tone);
        cv.put("setting", w.setting);
        cv.put("lore", w.loreSummary);
        db.insertWithOnConflict("worlds", null, cv, SQLiteDatabase.CONFLICT_REPLACE);
    }

    private void insertPersonaDirect(SQLiteDatabase db, PersonaModel p) {
        ContentValues cv = new ContentValues();
        cv.put("id", p.id);
        cv.put("name", p.name);
        cv.put("description", p.description);
        cv.put("personality", p.personality);
        cv.put("is_default", p.isDefault ? 1 : 0);
        db.insertWithOnConflict("personas", null, cv, SQLiteDatabase.CONFLICT_REPLACE);
    }

    private void setConfigDirect(SQLiteDatabase db, String key, String value) {
        ContentValues cv = new ContentValues();
        cv.put("key", key);
        cv.put("value", value);
        db.insertWithOnConflict("ai_config", null, cv, SQLiteDatabase.CONFLICT_REPLACE);
    }

    // --- REPOSITORY METHODS ---
    public List<CharacterModel> getAllCharacters() {
        List<CharacterModel> list = new ArrayList<>();
        SQLiteDatabase db = getReadableDatabase();
        Cursor cursor = db.rawQuery("SELECT id, name, title, category, description, personality, speech_style, first_message, tags FROM characters ORDER BY name ASC", null);
        if (cursor != null) {
            while (cursor.moveToNext()) {
                list.add(new CharacterModel(
                        cursor.getString(0), cursor.getString(1), cursor.getString(2),
                        cursor.getString(3), cursor.getString(4), cursor.getString(5),
                        cursor.getString(6), cursor.getString(7), cursor.getString(8)
                ));
            }
            cursor.close();
        }
        return list;
    }

    public CharacterModel getCharacter(String id) {
        SQLiteDatabase db = getReadableDatabase();
        Cursor cursor = db.rawQuery("SELECT id, name, title, category, description, personality, speech_style, first_message, tags FROM characters WHERE id = ?", new String[]{id});
        CharacterModel c = null;
        if (cursor != null) {
            if (cursor.moveToFirst()) {
                c = new CharacterModel(
                        cursor.getString(0), cursor.getString(1), cursor.getString(2),
                        cursor.getString(3), cursor.getString(4), cursor.getString(5),
                        cursor.getString(6), cursor.getString(7), cursor.getString(8)
                );
            }
            cursor.close();
        }
        return c;
    }

    public void saveCharacter(CharacterModel c) {
        SQLiteDatabase db = getWritableDatabase();
        insertCharacterDirect(db, c);
    }

    public List<WorldModel> getAllWorlds() {
        List<WorldModel> list = new ArrayList<>();
        SQLiteDatabase db = getReadableDatabase();
        Cursor cursor = db.rawQuery("SELECT id, name, genre, tone, setting, lore FROM worlds ORDER BY name ASC", null);
        if (cursor != null) {
            while (cursor.moveToNext()) {
                list.add(new WorldModel(
                        cursor.getString(0), cursor.getString(1), cursor.getString(2),
                        cursor.getString(3), cursor.getString(4), cursor.getString(5)
                ));
            }
            cursor.close();
        }
        return list;
    }

    public List<PersonaModel> getAllPersonas() {
        List<PersonaModel> list = new ArrayList<>();
        SQLiteDatabase db = getReadableDatabase();
        Cursor cursor = db.rawQuery("SELECT id, name, description, personality, is_default FROM personas ORDER BY is_default DESC, name ASC", null);
        if (cursor != null) {
            while (cursor.moveToNext()) {
                list.add(new PersonaModel(
                        cursor.getString(0), cursor.getString(1), cursor.getString(2),
                        cursor.getString(3), cursor.getInt(4) == 1
                ));
            }
            cursor.close();
        }
        return list;
    }

    public void savePersona(PersonaModel p) {
        SQLiteDatabase db = getWritableDatabase();
        insertPersonaDirect(db, p);
    }

    public List<ChatMessageModel> getMessagesForCharacter(String characterId) {
        List<ChatMessageModel> list = new ArrayList<>();
        SQLiteDatabase db = getReadableDatabase();
        Cursor cursor = db.rawQuery("SELECT id, sender, role, text, timestamp, action_type FROM messages WHERE character_id = ? ORDER BY id ASC", new String[]{characterId});
        if (cursor != null) {
            while (cursor.moveToNext()) {
                list.add(new ChatMessageModel(
                        cursor.getLong(0), cursor.getString(1), cursor.getString(2),
                        cursor.getString(3), cursor.getLong(4), cursor.getString(5)
                ));
            }
            cursor.close();
        }
        return list;
    }

    public long saveMessage(String characterId, ChatMessageModel msg) {
        SQLiteDatabase db = getWritableDatabase();
        ContentValues cv = new ContentValues();
        cv.put("character_id", characterId);
        cv.put("sender", msg.sender);
        cv.put("role", msg.role);
        cv.put("text", msg.text);
        cv.put("timestamp", msg.timestamp);
        cv.put("action_type", msg.actionType);
        return db.insert("messages", null, cv);
    }

    public void clearMessagesForCharacter(String characterId) {
        SQLiteDatabase db = getWritableDatabase();
        db.delete("messages", "character_id = ?", new String[]{characterId});
    }

    public String getConfig(String key, String defaultValue) {
        SQLiteDatabase db = getReadableDatabase();
        Cursor cursor = db.rawQuery("SELECT value FROM ai_config WHERE key = ?", new String[]{key});
        String val = defaultValue;
        if (cursor != null) {
            if (cursor.moveToFirst()) {
                val = cursor.getString(0);
            }
            cursor.close();
        }
        return val;
    }

    public void setConfig(String key, String value) {
        SQLiteDatabase db = getWritableDatabase();
        setConfigDirect(db, key, value);
    }
}
