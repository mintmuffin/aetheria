package com.aetheria.roleplay.models;

public class CharacterModel {
    public String id;
    public String name;
    public String title;
    public String category;
    public String description;
    public String personality;
    public String speechStyle;
    public String firstMessage;
    public String tags;

    public CharacterModel() { }

    public CharacterModel(String id, String name, String title, String category, String description,
                          String personality, String speechStyle, String firstMessage, String tags) {
        this.id = id;
        this.name = name;
        this.title = title;
        this.category = category;
        this.description = description;
        this.personality = personality;
        this.speechStyle = speechStyle;
        this.firstMessage = firstMessage;
        this.tags = tags;
    }
}
