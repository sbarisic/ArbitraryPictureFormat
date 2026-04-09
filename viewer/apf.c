#include "apf.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>

/* ---------- low-level read helpers ---------- */

typedef struct {
    const uint8_t *data;
    size_t size;
    size_t pos;
} Reader;

static int read_u8(Reader *r, uint8_t *out) {
    if (r->pos >= r->size) return 0;
    *out = r->data[r->pos++];
    return 1;
}

static int read_i32(Reader *r, int32_t *out) {
    if (r->pos + 4 > r->size) return 0;
    uint32_t v = (uint32_t)r->data[r->pos]
               | ((uint32_t)r->data[r->pos+1] << 8)
               | ((uint32_t)r->data[r->pos+2] << 16)
               | ((uint32_t)r->data[r->pos+3] << 24);
    *out = (int32_t)v;
    r->pos += 4;
    return 1;
}

static int read_u16(Reader *r, uint16_t *out) {
    if (r->pos + 2 > r->size) return 0;
    *out = (uint16_t)((uint16_t)r->data[r->pos] | ((uint16_t)r->data[r->pos+1] << 8));
    r->pos += 2;
    return 1;
}

static int read_bytes(Reader *r, uint8_t *buf, int len) {
    if (r->pos + (size_t)len > r->size) return 0;
    memcpy(buf, r->data + r->pos, len);
    r->pos += len;
    return 1;
}

/* ---------- ARGB (file) -> RGBA (output) conversion ---------- */

static uint32_t argb_to_rgba(int32_t argb) {
    uint32_t a = ((uint32_t)argb >> 24) & 0xFF;
    uint32_t r = ((uint32_t)argb >> 16) & 0xFF;
    uint32_t g = ((uint32_t)argb >> 8)  & 0xFF;
    uint32_t b = ((uint32_t)argb)       & 0xFF;
    return r | (g << 8) | (b << 16) | (a << 24);
}

/* ---------- RLE decode ---------- */

static uint8_t *rle_decode(const uint8_t *data, int data_len, int decoded_len) {
    uint8_t *result = (uint8_t *)calloc(decoded_len, 1);
    if (!result) return NULL;
    int ri = 0, di = 0;

    while (di < data_len && ri < decoded_len) {
        uint8_t header = data[di++];
        if (header & 0x80) {
            int count = (header & 0x7F) + 2;
            uint8_t val = data[di++];
            for (int j = 0; j < count && ri < decoded_len; j++)
                result[ri++] = val;
        } else {
            int count = (header & 0x7F) + 1;
            for (int j = 0; j < count && ri < decoded_len; j++)
                result[ri++] = data[di++];
        }
    }
    return result;
}

/* ---------- LZ77 decode ---------- */

static uint8_t *lz77_decode(const uint8_t *data, int data_len, int decoded_len) {
    uint8_t *result = (uint8_t *)calloc(decoded_len, 1);
    if (!result) return NULL;
    int ri = 0, di = 0;

    while (di < data_len && ri < decoded_len) {
        uint8_t header = data[di++];
        if (header & 0x80) {
            int len = (header & 0x7F) + 3;
            int dist = data[di] | (data[di+1] << 8);
            di += 2;
            int src = ri - dist;
            for (int j = 0; j < len && ri < decoded_len; j++)
                result[ri++] = result[src + j];
        } else {
            int len = (header & 0x7F) + 1;
            for (int j = 0; j < len && ri < decoded_len; j++)
                result[ri++] = data[di++];
        }
    }
    return result;
}

/* ---------- Decompress (mode byte + RLE or LZ77) ---------- */

static uint8_t *decompress(const uint8_t *data, int data_len, int decoded_len) {
    if (data_len == 0) return (uint8_t *)calloc(decoded_len, 1);
    uint8_t mode = data[0];
    const uint8_t *payload = data + 1;
    int payload_len = data_len - 1;

    if (mode == 0)
        return rle_decode(payload, payload_len, decoded_len);
    else
        return lz77_decode(payload, payload_len, decoded_len);
}

/* Read compressed blob: int32 len + bytes -> decompress */
static uint8_t *read_compressed(Reader *r, int decoded_len) {
    int32_t comp_len;
    if (!read_i32(r, &comp_len) || comp_len < 0) return NULL;
    uint8_t *comp = (uint8_t *)malloc(comp_len);
    if (!comp) return NULL;
    if (!read_bytes(r, comp, comp_len)) { free(comp); return NULL; }
    uint8_t *result = decompress(comp, comp_len, decoded_len);
    free(comp);
    return result;
}

/* ---------- Delta decode ---------- */

static void delta_decode_inplace(uint8_t *data, int len) {
    for (int i = 1; i < len; i++)
        data[i] = (uint8_t)(data[i] + data[i-1]);
}

/* Read compressed + delta-decoded plane */
static uint8_t *read_compressed_plane(Reader *r, int pixel_count) {
    uint8_t *delta = read_compressed(r, pixel_count);
    if (!delta) return NULL;
    delta_decode_inplace(delta, pixel_count);
    return delta;
}

/* ---------- Bit unpacking ---------- */

static uint8_t *unpack_bits(const uint8_t *packed, uint8_t bits_per_value, int count) {
    uint8_t *result = (uint8_t *)malloc(count);
    if (!result) return NULL;
    uint8_t mask = (uint8_t)((1 << bits_per_value) - 1);
    int bit_pos = 0;

    for (int i = 0; i < count; i++) {
        int byte_idx = bit_pos / 8;
        int bit_offset = bit_pos % 8;
        int val = packed[byte_idx] >> bit_offset;
        if (bit_offset + bits_per_value > 8)
            val |= packed[byte_idx + 1] << (8 - bit_offset);
        result[i] = (uint8_t)(val & mask);
        bit_pos += bits_per_value;
    }
    return result;
}

/* ---------- Paeth prediction ---------- */

static uint8_t paeth_predict(uint8_t a, uint8_t b, uint8_t c) {
    int p = (int)a + (int)b - (int)c;
    int pa = abs(p - a);
    int pb = abs(p - b);
    int pc = abs(p - c);
    if (pa <= pb && pa <= pc) return a;
    if (pb <= pc) return b;
    return c;
}

static uint8_t *paeth_decode(const uint8_t *residuals, int w, int h) {
    int total = w * h;
    uint8_t *result = (uint8_t *)malloc(total);
    if (!result) return NULL;

    for (int y = 0; y < h; y++) {
        for (int x = 0; x < w; x++) {
            int i = y * w + x;
            uint8_t a = x > 0 ? result[i-1] : 0;
            uint8_t b = y > 0 ? result[i-w] : 0;
            uint8_t c = (x > 0 && y > 0) ? result[i-w-1] : 0;
            result[i] = (uint8_t)(residuals[i] + paeth_predict(a, b, c));
        }
    }
    return result;
}

/* Read Paeth-compressed plane: compressed -> decompress -> paeth_decode */
static uint8_t *read_paeth_plane(Reader *r, int w, int h) {
    uint8_t *residuals = read_compressed(r, w * h);
    if (!residuals) return NULL;
    uint8_t *plane = paeth_decode(residuals, w, h);
    free(residuals);
    return plane;
}

/* ---------- Morton / Z-order ---------- */

static uint32_t morton_spread(uint32_t x) {
    x = (x | (x << 8)) & 0x00FF00FF;
    x = (x | (x << 4)) & 0x0F0F0F0F;
    x = (x | (x << 2)) & 0x33333333;
    x = (x | (x << 1)) & 0x55555555;
    return x;
}

static uint32_t morton_encode(uint32_t x, uint32_t y) {
    return morton_spread(x) | (morton_spread(y) << 1);
}

/* Returns array where result[i] = scanline index of the i-th pixel in Z-order */
static int *generate_z_order_indices(int width, int height) {
    int total = width * height;
    int *indices = (int *)malloc(total * sizeof(int));
    uint32_t *codes = (uint32_t *)malloc(total * sizeof(uint32_t));
    if (!indices || !codes) { free(indices); free(codes); return NULL; }

    for (int y = 0; y < height; y++) {
        for (int x = 0; x < width; x++) {
            int idx = y * width + x;
            indices[idx] = idx;
            codes[idx] = morton_encode((uint32_t)x, (uint32_t)y);
        }
    }

    /* Sort indices by morton code (simple insertion sort for correctness; could optimize) */
    for (int i = 1; i < total; i++) {
        uint32_t key_code = codes[i];
        int key_idx = indices[i];
        int j = i - 1;
        while (j >= 0 && codes[j] > key_code) {
            codes[j+1] = codes[j];
            indices[j+1] = indices[j];
            j--;
        }
        codes[j+1] = key_code;
        indices[j+1] = key_idx;
    }

    free(codes);
    return indices;
}

/* Reorder Z-order pixels back to scanline-order ImageData */
static uint32_t *reorder_from_z_order(int *z_order, uint32_t *z_pixels, int pixel_count,
                                       uint8_t *stencil_bits, int total_pixels) {
    /* Build scan-to-image index map */
    int *scan_to_img = (int *)malloc(total_pixels * sizeof(int));
    if (!scan_to_img) return NULL;
    int img_idx = 0;
    for (int i = 0; i < total_pixels; i++) {
        int byte_idx = i / 8;
        int bit_idx = i % 8;
        if (stencil_bits[byte_idx] & (1 << bit_idx))
            scan_to_img[i] = img_idx++;
        else
            scan_to_img[i] = -1;
    }

    uint32_t *image_data = (uint32_t *)calloc(pixel_count, sizeof(uint32_t));
    if (!image_data) { free(scan_to_img); return NULL; }

    int zi = 0;
    for (int i = 0; i < total_pixels; i++) {
        int si = scan_to_img[z_order[i]];
        if (si >= 0)
            image_data[si] = z_pixels[zi++];
    }

    free(scan_to_img);
    return image_data;
}

/* ---------- Fill plane helper ---------- */

static uint8_t *fill_plane(int count, uint8_t val) {
    uint8_t *p = (uint8_t *)malloc(count);
    if (p) memset(p, val, count);
    return p;
}

/* ---------- Variable-width int decode ---------- */

static int *bytes_to_ints(const uint8_t *data, uint8_t width, int count) {
    int *result = (int *)malloc(count * sizeof(int));
    if (!result) return NULL;
    for (int i = 0; i < count; i++) {
        int off = i * width;
        if (width == 1)
            result[i] = data[off];
        else if (width == 2)
            result[i] = data[off] | (data[off+1] << 8);
        else
            result[i] = data[off] | (data[off+1] << 8) | (data[off+2] << 16) | (data[off+3] << 24);
    }
    return result;
}

/* ---------- Stencil decode ---------- */

typedef struct {
    int width, height;
    uint8_t *bits; /* packed bits, LSB-first within each byte, scanline order */
} Stencil;

static int stencil_get(const Stencil *s, int x, int y) {
    int i = y * s->width + x;
    return (s->bits[i / 8] >> (i % 8)) & 1;
}

static int stencil_count(const Stencil *s) {
    int total = s->width * s->height;
    int count = 0;
    for (int i = 0; i < total; i++)
        if ((s->bits[i/8] >> (i%8)) & 1) count++;
    return count;
}

static int decode_stencil(Reader *r, Stencil *s) {
    int32_t w, h;
    if (!read_i32(r, &w) || !read_i32(r, &h)) return 0;
    s->width = w;
    s->height = h;

    int32_t raw_len, comp_len;
    if (!read_i32(r, &raw_len) || !read_i32(r, &comp_len)) return 0;

    int total = w * h;
    int byte_count = (total + 7) / 8;
    s->bits = (uint8_t *)malloc(byte_count);
    if (!s->bits) return 0;

    if (raw_len == 0 && comp_len == 0) {
        /* Full coverage sentinel */
        memset(s->bits, 0xFF, byte_count);
        /* Clear trailing bits */
        int extra = byte_count * 8 - total;
        if (extra > 0)
            s->bits[byte_count - 1] &= (uint8_t)((1 << (8 - extra)) - 1);
        return 1;
    }

    /* Read compressed Z-ordered stencil */
    uint8_t *comp = (uint8_t *)malloc(comp_len);
    if (!comp) return 0;
    if (!read_bytes(r, comp, comp_len)) { free(comp); return 0; }

    uint8_t *z_bits = decompress(comp, comp_len, raw_len);
    free(comp);
    if (!z_bits) return 0;

    /* Generate Z-order and reorder back to scanline */
    int *z_order = generate_z_order_indices(w, h);
    if (!z_order) { free(z_bits); return 0; }

    memset(s->bits, 0, byte_count);
    for (int i = 0; i < total; i++) {
        int src_byte = i / 8;
        int src_bit = i % 8;
        int bit_val = (z_bits[src_byte] >> src_bit) & 1;
        if (bit_val) {
            int dst = z_order[i];
            s->bits[dst / 8] |= (uint8_t)(1 << (dst % 8));
        }
    }

    free(z_bits);
    free(z_order);
    return 1;
}

/* ---------- Decode Mode 0: ChannelPlanes ---------- */

static uint32_t *decode_channel_planes(Reader *r, int pixel_count, Stencil *stencil) {
    uint8_t flags;
    if (!read_u8(r, &flags)) return NULL;
    int is_mono = flags & 1;
    int has_r = flags & 2;
    int has_g = flags & 4;
    int has_b = flags & 8;
    int has_a = flags & 16;

    uint8_t def_r, def_g, def_b, def_a;
    if (!read_u8(r, &def_r) || !read_u8(r, &def_g) ||
        !read_u8(r, &def_b) || !read_u8(r, &def_a)) return NULL;

    uint8_t *r_plane, *g_plane, *b_plane, *a_plane;

    if (is_mono) {
        r_plane = has_r ? read_compressed_plane(r, pixel_count) : fill_plane(pixel_count, def_r);
        g_plane = r_plane;
        b_plane = r_plane;
    } else {
        r_plane = has_r ? read_compressed_plane(r, pixel_count) : fill_plane(pixel_count, def_r);
        g_plane = has_g ? read_compressed_plane(r, pixel_count) : fill_plane(pixel_count, def_g);
        b_plane = has_b ? read_compressed_plane(r, pixel_count) : fill_plane(pixel_count, def_b);
    }
    a_plane = has_a ? read_compressed_plane(r, pixel_count) : fill_plane(pixel_count, def_a);

    if (!r_plane || !a_plane || (!is_mono && (!g_plane || !b_plane)))
        goto fail;

    /* Build z-ordered RGBA pixels */
    uint32_t *z_pixels = (uint32_t *)malloc(pixel_count * sizeof(uint32_t));
    if (!z_pixels) goto fail;

    for (int i = 0; i < pixel_count; i++) {
        z_pixels[i] = (uint32_t)r_plane[i]
                     | ((uint32_t)g_plane[i] << 8)
                     | ((uint32_t)b_plane[i] << 16)
                     | ((uint32_t)a_plane[i] << 24);
    }

    int total = stencil->width * stencil->height;
    int *z_order = generate_z_order_indices(stencil->width, stencil->height);
    uint32_t *result = reorder_from_z_order(z_order, z_pixels, pixel_count, stencil->bits, total);

    free(z_order);
    free(z_pixels);
    if (!is_mono) { free(g_plane); free(b_plane); }
    free(r_plane);
    free(a_plane);
    return result;

fail:
    if (is_mono) {
        free(r_plane);
    } else {
        free(r_plane); free(g_plane); free(b_plane);
    }
    free(a_plane);
    return NULL;
}

/* ---------- Decode Mode 1: PaletteIndexed ---------- */

static uint32_t *decode_palette_indexed(Reader *r, int pixel_count, Stencil *stencil) {
    uint16_t palette_count;
    if (!read_u16(r, &palette_count)) return NULL;

    uint32_t *palette = (uint32_t *)malloc(palette_count * sizeof(uint32_t));
    if (!palette) return NULL;
    for (int i = 0; i < palette_count; i++) {
        int32_t argb;
        if (!read_i32(r, &argb)) { free(palette); return NULL; }
        palette[i] = argb_to_rgba(argb);
    }

    uint8_t bits_per_index;
    if (!read_u8(r, &bits_per_index)) { free(palette); return NULL; }

    int packed_len = (bits_per_index == 8) ? pixel_count : (pixel_count * bits_per_index + 7) / 8;

    /* Read compressed delta packed indices */
    uint8_t *delta = read_compressed(r, packed_len);
    if (!delta) { free(palette); return NULL; }
    delta_decode_inplace(delta, packed_len);

    uint8_t *indices;
    if (bits_per_index == 8) {
        indices = delta;
    } else {
        indices = unpack_bits(delta, bits_per_index, pixel_count);
        free(delta);
        if (!indices) { free(palette); return NULL; }
    }

    /* Build z-ordered RGBA pixels */
    uint32_t *z_pixels = (uint32_t *)malloc(pixel_count * sizeof(uint32_t));
    if (!z_pixels) { free(indices); free(palette); return NULL; }
    for (int i = 0; i < pixel_count; i++)
        z_pixels[i] = palette[indices[i]];

    free(indices);
    free(palette);

    int total = stencil->width * stencil->height;
    int *z_order = generate_z_order_indices(stencil->width, stencil->height);
    uint32_t *result = reorder_from_z_order(z_order, z_pixels, pixel_count, stencil->bits, total);
    free(z_order);
    free(z_pixels);
    return result;
}

/* ---------- Decode Mode 2: ColorSorted ---------- */

static uint32_t *decode_color_sorted(Reader *r, int pixel_count) {
    int32_t unique_count;
    if (!read_i32(r, &unique_count)) return NULL;

    int32_t *colors = (int32_t *)malloc(unique_count * sizeof(int32_t));
    int32_t *counts = (int32_t *)malloc(unique_count * sizeof(int32_t));
    if (!colors || !counts) { free(colors); free(counts); return NULL; }

    for (int i = 0; i < unique_count; i++)
        if (!read_i32(r, &colors[i])) { free(colors); free(counts); return NULL; }
    for (int i = 0; i < unique_count; i++)
        if (!read_i32(r, &counts[i])) { free(colors); free(counts); return NULL; }

    uint8_t pos_width;
    if (!read_u8(r, &pos_width)) { free(colors); free(counts); return NULL; }

    uint8_t *pos_bytes = read_compressed(r, pixel_count * pos_width);
    if (!pos_bytes) { free(colors); free(counts); return NULL; }

    int *pos_deltas = bytes_to_ints(pos_bytes, pos_width, pixel_count);
    free(pos_bytes);
    if (!pos_deltas) { free(colors); free(counts); return NULL; }

    uint32_t *image_data = (uint32_t *)calloc(pixel_count, sizeof(uint32_t));
    if (!image_data) { free(colors); free(counts); free(pos_deltas); return NULL; }

    int di = 0;
    for (int c = 0; c < unique_count; c++) {
        uint32_t rgba = argb_to_rgba(colors[c]);
        int pos = 0;
        for (int j = 0; j < counts[c]; j++) {
            if (j == 0)
                pos = pos_deltas[di++];
            else
                pos += pos_deltas[di++];
            if (pos >= 0 && pos < pixel_count)
                image_data[pos] = rgba;
        }
    }

    free(colors);
    free(counts);
    free(pos_deltas);
    return image_data;
}

/* ---------- Decode Mode 3: SolidFill ---------- */

static uint32_t *decode_solid_fill(Reader *r, int pixel_count) {
    int32_t argb;
    if (!read_i32(r, &argb)) return NULL;
    uint32_t rgba = argb_to_rgba(argb);
    uint32_t *data = (uint32_t *)malloc(pixel_count * sizeof(uint32_t));
    if (!data) return NULL;
    for (int i = 0; i < pixel_count; i++)
        data[i] = rgba;
    return data;
}

/* ---------- Decode Mode 4: MonoAlpha ---------- */

static uint32_t *decode_mono_alpha(Reader *r, int pixel_count, Stencil *stencil) {
    uint8_t *luma = read_compressed_plane(r, pixel_count);
    if (!luma) return NULL;

    uint8_t has_alpha;
    if (!read_u8(r, &has_alpha)) { free(luma); return NULL; }

    uint8_t *alpha;
    if (has_alpha) {
        alpha = read_compressed_plane(r, pixel_count);
    } else {
        uint8_t const_alpha;
        if (!read_u8(r, &const_alpha)) { free(luma); return NULL; }
        alpha = fill_plane(pixel_count, const_alpha);
    }
    if (!alpha) { free(luma); return NULL; }

    uint32_t *z_pixels = (uint32_t *)malloc(pixel_count * sizeof(uint32_t));
    if (!z_pixels) { free(luma); free(alpha); return NULL; }
    for (int i = 0; i < pixel_count; i++) {
        uint8_t l = luma[i];
        z_pixels[i] = (uint32_t)l | ((uint32_t)l << 8) | ((uint32_t)l << 16) | ((uint32_t)alpha[i] << 24);
    }
    free(luma);
    free(alpha);

    int total = stencil->width * stencil->height;
    int *z_order = generate_z_order_indices(stencil->width, stencil->height);
    uint32_t *result = reorder_from_z_order(z_order, z_pixels, pixel_count, stencil->bits, total);
    free(z_order);
    free(z_pixels);
    return result;
}

/* ---------- Decode Mode 5: PaethFullGrid ---------- */

static uint32_t *decode_paeth_full_grid(Reader *r, int pixel_count, Stencil *stencil) {
    int w = stencil->width, h = stencil->height;

    uint8_t channel_flags;
    if (!read_u8(r, &channel_flags)) return NULL;
    int has_r = channel_flags & 1;
    int has_g = channel_flags & 2;
    int has_b = channel_flags & 4;
    int has_a = channel_flags & 8;
    int is_mono = channel_flags & 16;

    uint8_t *r_plane, *g_plane, *b_plane, *a_plane;
    uint8_t val;

    if (has_r)
        r_plane = read_paeth_plane(r, w, h);
    else {
        if (!read_u8(r, &val)) return NULL;
        r_plane = fill_plane(w * h, val);
    }
    if (!r_plane) return NULL;

    if (is_mono) {
        g_plane = r_plane;
        b_plane = r_plane;
    } else {
        if (has_g)
            g_plane = read_paeth_plane(r, w, h);
        else {
            if (!read_u8(r, &val)) { free(r_plane); return NULL; }
            g_plane = fill_plane(w * h, val);
        }
        if (has_b)
            b_plane = read_paeth_plane(r, w, h);
        else {
            if (!read_u8(r, &val)) { free(r_plane); free(g_plane); return NULL; }
            b_plane = fill_plane(w * h, val);
        }
    }

    if (has_a)
        a_plane = read_paeth_plane(r, w, h);
    else {
        if (!read_u8(r, &val)) goto paeth_fail;
        a_plane = fill_plane(w * h, val);
    }
    if (!a_plane) goto paeth_fail;

    {
        uint32_t *image_data = (uint32_t *)malloc(pixel_count * sizeof(uint32_t));
        if (!image_data) goto paeth_fail;

        int idx = 0;
        for (int y = 0; y < h; y++) {
            for (int x = 0; x < w; x++) {
                if (stencil_get(stencil, x, y)) {
                    int i = y * w + x;
                    image_data[idx++] = (uint32_t)r_plane[i]
                                      | ((uint32_t)g_plane[i] << 8)
                                      | ((uint32_t)b_plane[i] << 16)
                                      | ((uint32_t)a_plane[i] << 24);
                }
            }
        }

        if (!is_mono) { free(g_plane); free(b_plane); }
        free(r_plane);
        free(a_plane);
        return image_data;
    }

paeth_fail:
    if (!is_mono) { free(g_plane); free(b_plane); }
    free(r_plane);
    free(a_plane);
    return NULL;
}

/* ---------- Public API ---------- */

ApfImage *apf_load(const char *path) {
    FILE *f = fopen(path, "rb");
    if (!f) return NULL;

    fseek(f, 0, SEEK_END);
    long file_size = ftell(f);
    fseek(f, 0, SEEK_SET);

    uint8_t *file_data = (uint8_t *)malloc(file_size);
    if (!file_data) { fclose(f); return NULL; }
    if ((long)fread(file_data, 1, file_size, f) != file_size) {
        free(file_data);
        fclose(f);
        return NULL;
    }
    fclose(f);

    Reader reader = { file_data, (size_t)file_size, 0 };

    /* Version check */
    uint8_t version;
    if (!read_u8(&reader, &version) || version != 0x10) {
        fprintf(stderr, "apf_load: unknown version 0x%02X\n", version);
        free(file_data);
        return NULL;
    }

    /* Stencil (includes width/height) */
    Stencil stencil = {0};
    if (!decode_stencil(&reader, &stencil)) {
        free(file_data);
        return NULL;
    }

    /* Background + pixel count + encoding mode */
    int32_t bg_argb, pixel_count;
    uint8_t mode;
    if (!read_i32(&reader, &bg_argb) || !read_i32(&reader, &pixel_count) || !read_u8(&reader, &mode)) {
        free(stencil.bits);
        free(file_data);
        return NULL;
    }
    uint32_t bg_rgba = argb_to_rgba(bg_argb);

    /* Decode pixel data based on mode */
    uint32_t *image_data = NULL;
    switch (mode) {
        case 0: image_data = decode_channel_planes(&reader, pixel_count, &stencil); break;
        case 1: image_data = decode_palette_indexed(&reader, pixel_count, &stencil); break;
        case 2: image_data = decode_color_sorted(&reader, pixel_count); break;
        case 3: image_data = decode_solid_fill(&reader, pixel_count); break;
        case 4: image_data = decode_mono_alpha(&reader, pixel_count, &stencil); break;
        case 5: image_data = decode_paeth_full_grid(&reader, pixel_count, &stencil); break;
        default:
            fprintf(stderr, "apf_load: unknown encoding mode %d\n", mode);
            break;
    }

    free(file_data);

    if (!image_data) {
        free(stencil.bits);
        return NULL;
    }

    /* Compose final RGBA pixel buffer with background fill */
    int w = stencil.width, h = stencil.height;
    int total = w * h;
    uint32_t *pixels = (uint32_t *)malloc(total * sizeof(uint32_t));
    if (!pixels) {
        free(image_data);
        free(stencil.bits);
        return NULL;
    }

    int img_idx = 0;
    for (int i = 0; i < total; i++) {
        if ((stencil.bits[i/8] >> (i%8)) & 1)
            pixels[i] = image_data[img_idx++];
        else
            pixels[i] = bg_rgba;
    }

    free(image_data);
    free(stencil.bits);

    ApfImage *img = (ApfImage *)malloc(sizeof(ApfImage));
    if (!img) { free(pixels); return NULL; }
    img->width = w;
    img->height = h;
    img->pixels = pixels;
    return img;
}

void apf_free(ApfImage *img) {
    if (img) {
        free(img->pixels);
        free(img);
    }
}
