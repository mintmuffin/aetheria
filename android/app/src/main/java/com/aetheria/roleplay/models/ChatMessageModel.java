package com.aetheria.roleplay.models;

public class ChatMessageModel {
    public long id;
    public String sender;
    public String role; // "user", "assistant", "system"
    public String text;
    public long timestamp;
    public String actionType; // "chat", "action", "whisper", "dice"

    public ChatMessageModel() { }

    public ChatMessageModel(long id, String sender, String role, String text, long timestamp, String actionType) {
        this.id = id;
        this.sender = sender;
        this.role = role;
        this.text = text;
        this.timestamp = timestamp;
        this.actionType = actionType;
    }
}
