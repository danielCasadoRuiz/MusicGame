package com.tempofury.music;

import android.app.Activity;
import android.app.Fragment;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;
import android.provider.OpenableColumns;
import java.io.*;
import java.util.Locale;

/** Storage Access Framework: no broad storage permission; original documents are never modified. */
public class AudioPicker extends Fragment {
    private static volatile String picked = "";
    public static String result() { return picked; }
    public static void open(Activity activity) {
        picked = "__PENDING__";
        activity.runOnUiThread(() -> {
            if (activity.getFragmentManager().findFragmentByTag("TFMusicPicker") != null) { picked = ""; return; }
            activity.getFragmentManager().beginTransaction().add(new AudioPicker(), "TFMusicPicker").commitAllowingStateLoss();
        });
    }
    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        if (state != null) return;
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        intent.setType("audio/*");
        try { startActivityForResult(intent, 871); } catch (Exception e) { finish(""); }
    }
    @Override public void onActivityResult(int request, int code, Intent data) {
        if (request != 871) return;
        if (code != Activity.RESULT_OK || data == null || data.getData() == null) { finish(""); return; }
        Uri uri = data.getData();
        final Activity activity = getActivity();
        if (activity == null) { picked = ""; return; }
        new Thread(() -> {
            File target = null;
            try {
                String name = "audio";
                try (Cursor cursor = activity.getContentResolver().query(uri, new String[]{OpenableColumns.DISPLAY_NAME}, null, null, null)) {
                    if (cursor != null && cursor.moveToFirst()) name = cursor.getString(0);
                }
                // Keep a safe display name and extension; discard all path components.
                name = new File(name).getName().replaceAll("[^\\p{L}\\p{N} ._()—-]", "_");
                String lower = name.toLowerCase(Locale.ROOT);
                if (!(lower.endsWith(".wav") || lower.endsWith(".mp3") || lower.endsWith(".ogg") || lower.endsWith(".aif") || lower.endsWith(".aiff"))) throw new IOException("Unsupported format");
                File dir = new File(activity.getCacheDir(), "tf-audio-picker"); dir.mkdirs();
                File[] old = dir.listFiles(); if (old != null) for (File f : old) f.delete();
                target = new File(dir, name);
                try (InputStream input = activity.getContentResolver().openInputStream(uri); OutputStream output = new FileOutputStream(target)) {
                    if (input == null) throw new IOException("Cannot read document");
                    byte[] buffer = new byte[65536]; int count;
                    while ((count = input.read(buffer)) != -1) output.write(buffer, 0, count);
                }
                String path = target.getAbsolutePath(); activity.runOnUiThread(() -> finish(path));
            } catch (Exception e) {
                if (target != null) target.delete();
                activity.runOnUiThread(() -> finish(""));
            }
        }, "TF-AudioImport").start();
    }
    private void finish(String path) {
        picked = path;
        if (getFragmentManager() != null) getFragmentManager().beginTransaction().remove(this).commitAllowingStateLoss();
    }
}
