package com.aetheria.roleplay.models;

public class WorldModel {
    public String id;
    public String name;
    public String genre;
    public String tone;
    public String setting;
    public String loreSummary;

    public WorldModel() { }

    public WorldModel(String id, String name, String genre, String tone, String setting, String loreSummary) {
        this.id = id;
        this.name = name;
        this.genre = genre;
        this.tone = tone;
        this.setting = setting;
        this.loreSummary = loreSummary;
    }
}
