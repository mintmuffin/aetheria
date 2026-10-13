package com.aetheria.roleplay.models;

public class PersonaModel {
    public String id;
    public String name;
    public String description;
    public String personality;
    public boolean isDefault;

    public PersonaModel() { }

    public PersonaModel(String id, String name, String description, String personality, boolean isDefault) {
        this.id = id;
        this.name = name;
        this.description = description;
        this.personality = personality;
        this.isDefault = isDefault;
    }
}
