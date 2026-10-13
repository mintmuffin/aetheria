package com.aetheria.roleplay.ui;

import java.util.Random;

public class DiceRoller {
    private static final Random sRandom = new Random();

    public static class RollResult {
        public int dieSides;
        public int rollValue;
        public int modifier;
        public int total;
        public String description;

        public RollResult(int dieSides, int rollValue, int modifier, int total, String description) {
            this.dieSides = dieSides;
            this.rollValue = rollValue;
            this.modifier = modifier;
            this.total = total;
            this.description = description;
        }
    }

    public static RollResult roll(int sides, int modifier) {
        int raw = sRandom.nextInt(sides) + 1;
        int total = raw + modifier;
        StringBuilder sb = new StringBuilder();
        sb.append("🎲 Rolled d").append(sides).append(": [").append(raw).append("]");
        if (modifier != 0) {
            if (modifier > 0) sb.append(" + ").append(modifier);
            else sb.append(" - ").append(Math.abs(modifier));
            sb.append(" = ").append(total);
        }
        if (sides == 20) {
            if (raw == 20) sb.append(" ★ Natural 20! Critical Hit! ★");
            else if (raw == 1) sb.append(" ✖ Natural 1! Critical Fail! ✖");
        }
        return new RollResult(sides, raw, modifier, total, sb.toString());
    }
}
