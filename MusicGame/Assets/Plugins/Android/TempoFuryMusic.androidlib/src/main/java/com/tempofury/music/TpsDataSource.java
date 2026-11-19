package com.tempofury.music;

import android.media.MediaDataSource;
import android.util.Base64;
import java.io.*;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.security.MessageDigest;
import java.util.Arrays;
import javax.crypto.Cipher;
import javax.crypto.Mac;
import javax.crypto.spec.IvParameterSpec;
import javax.crypto.spec.SecretKeySpec;

/** Authenticated offline TPS audio. Plain audio exists only in memory, never in a temporary file. */
public final class TpsDataSource extends MediaDataSource {
    private byte[] audio;
    public TpsDataSource(String path, String encodedKeys) throws Exception {
        File file = new File(path);
        if (file.length() < 96 || file.length() > 536871008L) throw new IOException("Invalid TPS length");
        byte[] bytes = new byte[(int)file.length()];
        try (DataInputStream input = new DataInputStream(new FileInputStream(file))) { input.readFully(bytes); }
        byte[] keys = Base64.decode(encodedKeys, Base64.NO_WRAP);
        try {
            int cipherLength = bytes.length - 80;
            long length = ByteBuffer.wrap(bytes, 40, 8).order(ByteOrder.LITTLE_ENDIAN).getLong();
            if (keys.length != 64 || bytes[0] != 84 || bytes[1] != 80 || bytes[2] != 83 || bytes[3] != 49 || bytes[4] != 2 || bytes[5] != 1 || bytes[6] != 0 || bytes[7] != 0 ||
                length < 0 || length > 536870912L || cipherLength % 16 != 0 || length / 16 * 16 + 16 != cipherLength) throw new IOException("Invalid TPS header");
            Mac hmac = Mac.getInstance("HmacSHA256");
            hmac.init(new SecretKeySpec(keys, 32, 32, "HmacSHA256"));
            hmac.update(bytes, 0, bytes.length - 32);
            if (!MessageDigest.isEqual(hmac.doFinal(), Arrays.copyOfRange(bytes, bytes.length - 32, bytes.length))) throw new IOException("TPS authentication failed");
            Cipher cipher = Cipher.getInstance("AES/CBC/PKCS5Padding");
            cipher.init(Cipher.DECRYPT_MODE, new SecretKeySpec(keys, 0, 32, "AES"), new IvParameterSpec(bytes, 24, 16));
            audio = cipher.doFinal(bytes, 48, cipherLength);
            if (audio.length != length) { Arrays.fill(audio, (byte)0); audio = null; throw new IOException("TPS decoded length mismatch"); }
        } finally { Arrays.fill(keys, (byte)0); Arrays.fill(bytes, (byte)0); }
    }
    @Override public synchronized int readAt(long position, byte[] buffer, int offset, int size) throws IOException {
        if (audio == null) throw new IOException("TPS audio is closed");
        if (position < 0 || offset < 0 || size < 0 || offset > buffer.length - size) throw new IOException("Invalid TPS read range");
        if (size == 0) return 0;
        if (position >= audio.length) return -1;
        int count = (int)Math.min(size, audio.length - position);
        System.arraycopy(audio, (int)position, buffer, offset, count); return count;
    }
    @Override public synchronized long getSize() { return audio == null ? 0 : audio.length; }
    @Override public synchronized void close() { if (audio != null) Arrays.fill(audio, (byte)0); audio = null; }
}
