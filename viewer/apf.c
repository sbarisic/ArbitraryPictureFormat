#include "apf.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>
#include <limits.h>

/* ---------- low-level read helpers ---------- */

typedef struct {
	const uint8_t* data;
	size_t size;
	size_t pos;
} Reader;

#define APF_MAX_BYTES (256 * 1024 * 1024)
#define APF_MAX_STRING_BYTES (1024 * 1024)
#define APF_MAX_IMAGES 65536
#define APF_MAX_METADATA_ENTRIES 65536
#define APF_MAX_DIMENSION 1000000
#define APF_MAX_PIXELS 268435456

static int checked_len(int len) {
	return len >= 0 && len <= APF_MAX_BYTES;
}

static int checked_count(int count, int max_count) {
	return count >= 0 && count <= max_count;
}

static int checked_total_pixels(int width, int height, int* total) {
	long long v;
	if (width <= 0 || height <= 0) return 0;
	if (width > APF_MAX_DIMENSION || height > APF_MAX_DIMENSION) return 0;
	v = (long long)width * (long long)height;
	if (v > APF_MAX_PIXELS || v > INT_MAX) return 0;
	*total = (int)v;
	return 1;
}

static int checked_mul_int(int a, int b, int* out) {
	long long v;
	if (a < 0 || b < 0) return 0;
	v = (long long)a * (long long)b;
	if (v > INT_MAX || v > APF_MAX_BYTES) return 0;
	*out = (int)v;
	return 1;
}

static int checked_byte_count_for_bits(int bit_count, int* byte_count) {
	if (bit_count < 0 || bit_count > APF_MAX_PIXELS) return 0;
	*byte_count = (bit_count + 7) / 8;
	return 1;
}

static void* apf_malloc_array(size_t count, size_t elem_size) {
	if (elem_size != 0 && count > SIZE_MAX / elem_size) return NULL;
	if (count * elem_size > (size_t)APF_MAX_BYTES) return NULL;
	if (count == 0 || elem_size == 0) count = 1, elem_size = 1;
	return malloc(count * elem_size);
}

static void* apf_calloc_array(size_t count, size_t elem_size) {
	if (elem_size != 0 && count > SIZE_MAX / elem_size) return NULL;
	if (count * elem_size > (size_t)APF_MAX_BYTES) return NULL;
	if (count == 0 || elem_size == 0) count = 1, elem_size = 1;
	return calloc(count, elem_size);
}

static int read_u8(Reader* r, uint8_t* out) {
	if (r->pos >= r->size) return 0;
	*out = r->data[r->pos++];
	return 1;
}

static int read_i32(Reader* r, int32_t* out) {
	if (r->pos + 4 > r->size) return 0;
	uint32_t v = (uint32_t)r->data[r->pos]
		| ((uint32_t)r->data[r->pos + 1] << 8)
		| ((uint32_t)r->data[r->pos + 2] << 16)
		| ((uint32_t)r->data[r->pos + 3] << 24);
	*out = (int32_t)v;
	r->pos += 4;
	return 1;
}

static int read_u16(Reader* r, uint16_t* out) {
	if (r->pos + 2 > r->size) return 0;
	*out = (uint16_t)((uint16_t)r->data[r->pos] | ((uint16_t)r->data[r->pos + 1] << 8));
	r->pos += 2;
	return 1;
}

static int read_bytes(Reader* r, uint8_t* buf, int len) {
	if (!checked_len(len)) return 0;
	if ((size_t)len > r->size - r->pos) return 0;
	memcpy(buf, r->data + r->pos, len);
	r->pos += len;
	return 1;
}

static char* apf_strdup(const char* s) {
	size_t len;
	char* copy;
	if (!s) return NULL;
	len = strlen(s);
	if (len > (size_t)APF_MAX_STRING_BYTES) return NULL;
	copy = (char*)apf_malloc_array(len + 1, 1);
	if (!copy) return NULL;
	memcpy(copy, s, len + 1);
	return copy;
}

/* ---------- ARGB (file) -> RGBA (output) conversion ---------- */

static uint32_t argb_to_rgba(int32_t argb) {
	uint32_t a = ((uint32_t)argb >> 24) & 0xFF;
	uint32_t r = ((uint32_t)argb >> 16) & 0xFF;
	uint32_t g = ((uint32_t)argb >> 8) & 0xFF;
	uint32_t b = ((uint32_t)argb) & 0xFF;
	return r | (g << 8) | (b << 16) | (a << 24);
}

/* ---------- RLE decode ---------- */

static uint8_t* rle_decode(const uint8_t* data, int data_len, int decoded_len) {
	uint8_t* result;
	if (!checked_len(data_len) || !checked_len(decoded_len)) return NULL;
	result = (uint8_t*)apf_calloc_array((size_t)decoded_len, 1);
	if (!result) return NULL;
	int ri = 0, di = 0;

	while (di < data_len && ri < decoded_len) {
		uint8_t header = data[di++];
		if (header & 0x80) {
			int count = (header & 0x7F) + 2;
			if (di >= data_len) { free(result); return NULL; }
			uint8_t val = data[di++];
			for (int j = 0; j < count && ri < decoded_len; j++)
				result[ri++] = val;
		}
		else {
			int count = (header & 0x7F) + 1;
			if (data_len - di < count) { free(result); return NULL; }
			for (int j = 0; j < count && ri < decoded_len; j++)
				result[ri++] = data[di++];
		}
	}
	if (ri != decoded_len || di != data_len) { free(result); return NULL; }
	return result;
}

/* ---------- LZ77 decode ---------- */

static uint8_t* lz77_decode(const uint8_t* data, int data_len, int decoded_len) {
	uint8_t* result;
	if (!checked_len(data_len) || !checked_len(decoded_len)) return NULL;
	result = (uint8_t*)apf_calloc_array((size_t)decoded_len, 1);
	if (!result) return NULL;
	int ri = 0, di = 0;

	while (di < data_len && ri < decoded_len) {
		uint8_t header = data[di++];
		if (header & 0x80) {
			int len = (header & 0x7F) + 3;
			if (data_len - di < 2) { free(result); return NULL; }
			int dist = data[di] | (data[di + 1] << 8);
			di += 2;
			if (dist <= 0 || dist > ri) { free(result); return NULL; }
			int src = ri - dist;
			for (int j = 0; j < len && ri < decoded_len; j++)
				result[ri++] = result[src + j];
		}
		else {
			int len = (header & 0x7F) + 1;
			if (data_len - di < len) { free(result); return NULL; }
			for (int j = 0; j < len && ri < decoded_len; j++)
				result[ri++] = data[di++];
		}
	}
	if (ri != decoded_len || di != data_len) { free(result); return NULL; }
	return result;
}

/* ---------- rANS decode ---------- */

#define RANS_SCALE_BITS 12
#define RANS_SCALE (1 << RANS_SCALE_BITS) /* 4096 */
#define RANS_LOWER (1u << 23)

static uint8_t* rans_decode(const uint8_t* data, int data_len, int decoded_len) {
	if (!checked_len(data_len) || !checked_len(decoded_len)) return NULL;
	if (decoded_len == 0)
		return (uint8_t*)apf_calloc_array(1, 1);
	if (data_len < 6) return NULL;

	uint8_t* result = (uint8_t*)apf_malloc_array((size_t)decoded_len, 1);
	if (!result) return NULL;

	int pos = 0;

	/* Read frequency table */
	int num_symbols = data[pos] | (data[pos + 1] << 8);
	pos += 2;
	if (num_symbols <= 0 || num_symbols > 256) { free(result); return NULL; }
	if (data_len - pos < num_symbols * 3 + 4) { free(result); return NULL; }

	int freq[256] = { 0 };
	int cum_freq[257] = { 0 };
	int total_freq = 0;

	for (int i = 0; i < num_symbols; i++) {
		uint8_t sym = data[pos++];
		int f = data[pos] | (data[pos + 1] << 8);
		pos += 2;
		if (f <= 0 || freq[sym] != 0) { free(result); return NULL; }
		freq[sym] = f;
		total_freq += f;
	}
	if (total_freq != RANS_SCALE) { free(result); return NULL; }

	for (int i = 0; i < 256; i++)
		cum_freq[i + 1] = cum_freq[i] + freq[i];

	/* Reverse lookup table: cumulative freq -> symbol */
	uint8_t cum_to_sym[RANS_SCALE];
	memset(cum_to_sym, 0, sizeof(cum_to_sym));
	for (int s = 0; s < 256; s++)
		for (int j = cum_freq[s]; j < cum_freq[s + 1]; j++) {
			if (j < 0 || j >= RANS_SCALE) { free(result); return NULL; }
			cum_to_sym[j] = (uint8_t)s;
		}

	/* Read initial state */
	uint32_t state = (uint32_t)data[pos]
		| ((uint32_t)data[pos + 1] << 8)
		| ((uint32_t)data[pos + 2] << 16)
		| ((uint32_t)data[pos + 3] << 24);
	pos += 4;

	/* Decode symbols */
	for (int i = 0; i < decoded_len; i++) {
		uint32_t slot = state & (RANS_SCALE - 1);
		uint8_t sym = cum_to_sym[slot];
		if (freq[sym] == 0) { free(result); return NULL; }
		result[i] = sym;

		int fs = freq[sym];
		int cs = cum_freq[sym];

		state = (uint32_t)fs * (state >> RANS_SCALE_BITS) + slot - (uint32_t)cs;

		/* Renormalize: read bytes until state >= RANS_LOWER */
		while (state < RANS_LOWER && pos < data_len)
			state = (state << 8) | data[pos++];
		if (state < RANS_LOWER && pos >= data_len && i + 1 < decoded_len) {
			free(result);
			return NULL;
		}
	}

	return result;
}

/* ---------- Decompress (mode byte + RLE / LZ77 / rANS / LZ77+rANS) ---------- */

static uint8_t* decompress(const uint8_t* data, int data_len, int decoded_len) {
	if (!checked_len(data_len) || !checked_len(decoded_len)) return NULL;
	if (data_len == 0) {
		if (decoded_len == 0) return (uint8_t*)apf_calloc_array(1, 1);
		return NULL;
	}
	uint8_t mode = data[0];
	const uint8_t* payload = data + 1;
	int payload_len = data_len - 1;

	switch (mode) {
	case 0: return rle_decode(payload, payload_len, decoded_len);
	case 1: return lz77_decode(payload, payload_len, decoded_len);
	case 2: return rans_decode(payload, payload_len, decoded_len);
	case 3: {
		if (payload_len < 4) return NULL;
		int lz_len = payload[0] | (payload[1] << 8) | (payload[2] << 16) | (payload[3] << 24);
		if (!checked_len(lz_len)) return NULL;
		uint8_t* lz = rans_decode(payload + 4, payload_len - 4, lz_len);
		if (!lz) return NULL;
		uint8_t* result = lz77_decode(lz, lz_len, decoded_len);
		free(lz);
		return result;
	}
	default: return NULL;
	}
}

/* Read compressed blob: int32 len + bytes -> decompress */
static uint8_t* read_compressed(Reader* r, int decoded_len) {
	int32_t comp_len;
	if (!read_i32(r, &comp_len) || !checked_len(comp_len)) return NULL;
	uint8_t* comp = (uint8_t*)apf_malloc_array((size_t)comp_len, 1);
	if (!comp) return NULL;
	if (!read_bytes(r, comp, comp_len)) { free(comp); return NULL; }
	uint8_t* result = decompress(comp, comp_len, decoded_len);
	free(comp);
	return result;
}

/* ---------- Delta decode ---------- */

static void delta_decode_inplace(uint8_t* data, int len) {
	for (int i = 1; i < len; i++)
		data[i] = (uint8_t)(data[i] + data[i - 1]);
}

/* Read compressed + delta-decoded plane */
static uint8_t* read_compressed_plane(Reader* r, int pixel_count) {
	uint8_t* delta = read_compressed(r, pixel_count);
	if (!delta) return NULL;
	delta_decode_inplace(delta, pixel_count);
	return delta;
}

/* ---------- Bit unpacking ---------- */

static uint8_t* unpack_bits(const uint8_t* packed, uint8_t bits_per_value, int count) {
	uint8_t* result;
	int bit_count;
	int packed_len;
	if (!(bits_per_value == 1 || bits_per_value == 2 || bits_per_value == 4 || bits_per_value == 8)) return NULL;
	if (!checked_mul_int(count, bits_per_value, &bit_count)) return NULL;
	if (!checked_byte_count_for_bits(bit_count, &packed_len)) return NULL;
	result = (uint8_t*)apf_malloc_array((size_t)count, 1);
	if (!result) return NULL;
	uint8_t mask = (uint8_t)((1 << bits_per_value) - 1);
	int bit_pos = 0;

	for (int i = 0; i < count; i++) {
		int byte_idx = bit_pos / 8;
		int bit_offset = bit_pos % 8;
		if (byte_idx >= packed_len) { free(result); return NULL; }
		int val = packed[byte_idx] >> bit_offset;
		if (bit_offset + bits_per_value > 8) {
			if (byte_idx + 1 >= packed_len) { free(result); return NULL; }
			val |= packed[byte_idx + 1] << (8 - bit_offset);
		}
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

static uint8_t* paeth_decode(const uint8_t* residuals, int w, int h) {
	int total;
	if (!checked_total_pixels(w, h, &total)) return NULL;
	uint8_t* result = (uint8_t*)apf_malloc_array((size_t)total, 1);
	if (!result) return NULL;

	for (int y = 0; y < h; y++) {
		for (int x = 0; x < w; x++) {
			int i = y * w + x;
			uint8_t a = x > 0 ? result[i - 1] : 0;
			uint8_t b = y > 0 ? result[i - w] : 0;
			uint8_t c = (x > 0 && y > 0) ? result[i - w - 1] : 0;
			result[i] = (uint8_t)(residuals[i] + paeth_predict(a, b, c));
		}
	}
	return result;
}

/* Read Paeth-compressed plane: compressed -> decompress -> paeth_decode */
static uint8_t* read_paeth_plane(Reader* r, int w, int h) {
	int total;
	if (!checked_total_pixels(w, h, &total)) return NULL;
	uint8_t* residuals = read_compressed(r, total);
	if (!residuals) return NULL;
	uint8_t* plane = paeth_decode(residuals, w, h);
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
static int* generate_z_order_indices(int width, int height) {
	int total;
	if (!checked_total_pixels(width, height, &total)) return NULL;
	int* indices = (int*)apf_malloc_array((size_t)total, sizeof(int));
	uint32_t* codes = (uint32_t*)apf_malloc_array((size_t)total, sizeof(uint32_t));
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
			codes[j + 1] = codes[j];
			indices[j + 1] = indices[j];
			j--;
		}
		codes[j + 1] = key_code;
		indices[j + 1] = key_idx;
	}

	free(codes);
	return indices;
}

/* Reorder Z-order pixels back to scanline-order ImageData */
static uint32_t* reorder_from_z_order(int* z_order, uint32_t* z_pixels, int pixel_count,
	uint8_t* stencil_bits, int total_pixels) {
	if (!z_order || !z_pixels || !stencil_bits) return NULL;
	/* Build scan-to-image index map */
	int* scan_to_img = (int*)apf_malloc_array((size_t)total_pixels, sizeof(int));
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

	if (img_idx != pixel_count) { free(scan_to_img); return NULL; }

	uint32_t* image_data = (uint32_t*)apf_calloc_array((size_t)pixel_count, sizeof(uint32_t));
	if (!image_data) { free(scan_to_img); return NULL; }

	int zi = 0;
	for (int i = 0; i < total_pixels; i++) {
		int si = scan_to_img[z_order[i]];
		if (si >= 0) {
			if (zi >= pixel_count) { free(image_data); free(scan_to_img); return NULL; }
			image_data[si] = z_pixels[zi++];
		}
	}
	if (zi != pixel_count) { free(image_data); free(scan_to_img); return NULL; }

	free(scan_to_img);
	return image_data;
}

/* ---------- Fill plane helper ---------- */

static uint8_t* fill_plane(int count, uint8_t val) {
	if (!checked_len(count)) return NULL;
	uint8_t* p = (uint8_t*)apf_malloc_array((size_t)count, 1);
	if (p) memset(p, val, count);
	return p;
}

/* ---------- Variable-width int decode ---------- */

static int* bytes_to_ints(const uint8_t* data, uint8_t width, int count) {
	if (!(width == 1 || width == 2 || width == 4)) return NULL;
	int* result = (int*)apf_malloc_array((size_t)count, sizeof(int));
	if (!result) return NULL;
	for (int i = 0; i < count; i++) {
		int off = i * width;
		if (width == 1)
			result[i] = data[off];
		else if (width == 2)
			result[i] = data[off] | (data[off + 1] << 8);
		else
			result[i] = data[off] | (data[off + 1] << 8) | (data[off + 2] << 16) | (data[off + 3] << 24);
	}
	return result;
}

/* ---------- Stencil decode ---------- */

typedef struct {
	int width, height;
	uint8_t* bits; /* packed bits, LSB-first within each byte, scanline order */
} Stencil;

static int stencil_get(const Stencil* s, int x, int y) {
	int i = y * s->width + x;
	return (s->bits[i / 8] >> (i % 8)) & 1;
}

static int stencil_count(const Stencil* s) {
	int total;
	if (!checked_total_pixels(s->width, s->height, &total)) return -1;
	int count = 0;
	for (int i = 0; i < total; i++)
		if ((s->bits[i / 8] >> (i % 8)) & 1) count++;
	return count;
}

static int decode_stencil(Reader* r, Stencil* s) {
	int32_t w, h;
	if (!read_i32(r, &w) || !read_i32(r, &h)) return 0;
	s->width = w;
	s->height = h;

	int32_t raw_len, comp_len;
	if (!read_i32(r, &raw_len) || !read_i32(r, &comp_len)) return 0;

	int total;
	int byte_count;
	if (!checked_total_pixels(w, h, &total)) return 0;
	if (!checked_byte_count_for_bits(total, &byte_count)) return 0;
	if (!checked_len(raw_len) || !checked_len(comp_len)) return 0;

	s->bits = (uint8_t*)apf_malloc_array((size_t)byte_count, 1);
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

	if (raw_len != byte_count || comp_len <= 0) { free(s->bits); s->bits = NULL; return 0; }

	/* Read compressed Z-ordered stencil */
	uint8_t* comp = (uint8_t*)apf_malloc_array((size_t)comp_len, 1);
	if (!comp) { free(s->bits); s->bits = NULL; return 0; }
	if (!read_bytes(r, comp, comp_len)) { free(comp); free(s->bits); s->bits = NULL; return 0; }

	uint8_t* z_bits = decompress(comp, comp_len, raw_len);
	free(comp);
	if (!z_bits) { free(s->bits); s->bits = NULL; return 0; }

	/* Generate Z-order and reorder back to scanline */
	int* z_order = generate_z_order_indices(w, h);
	if (!z_order) { free(z_bits); free(s->bits); s->bits = NULL; return 0; }

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

static uint32_t* decode_channel_planes(Reader* r, int pixel_count, Stencil* stencil) {
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

	uint8_t* r_plane, * g_plane, * b_plane, * a_plane;

	if (is_mono) {
		r_plane = has_r ? read_compressed_plane(r, pixel_count) : fill_plane(pixel_count, def_r);
		g_plane = r_plane;
		b_plane = r_plane;
	}
	else {
		r_plane = has_r ? read_compressed_plane(r, pixel_count) : fill_plane(pixel_count, def_r);
		g_plane = has_g ? read_compressed_plane(r, pixel_count) : fill_plane(pixel_count, def_g);
		b_plane = has_b ? read_compressed_plane(r, pixel_count) : fill_plane(pixel_count, def_b);
	}
	a_plane = has_a ? read_compressed_plane(r, pixel_count) : fill_plane(pixel_count, def_a);

	if (!r_plane || !a_plane || (!is_mono && (!g_plane || !b_plane)))
		goto fail;

	/* Build z-ordered RGBA pixels */
	uint32_t* z_pixels = (uint32_t*)apf_malloc_array((size_t)pixel_count, sizeof(uint32_t));
	if (!z_pixels) goto fail;

	for (int i = 0; i < pixel_count; i++) {
		z_pixels[i] = (uint32_t)r_plane[i]
			| ((uint32_t)g_plane[i] << 8)
			| ((uint32_t)b_plane[i] << 16)
			| ((uint32_t)a_plane[i] << 24);
	}

	int total;
	if (!checked_total_pixels(stencil->width, stencil->height, &total)) { free(z_pixels); goto fail; }
	int* z_order = generate_z_order_indices(stencil->width, stencil->height);
	uint32_t* result = reorder_from_z_order(z_order, z_pixels, pixel_count, stencil->bits, total);

	free(z_order);
	free(z_pixels);
	if (!is_mono) { free(g_plane); free(b_plane); }
	free(r_plane);
	free(a_plane);
	return result;

fail:
	if (is_mono) {
		free(r_plane);
	}
	else {
		free(r_plane); free(g_plane); free(b_plane);
	}
	free(a_plane);
	return NULL;
}

/* ---------- Decode Mode 1: PaletteIndexed ---------- */

static uint32_t* decode_palette_indexed(Reader* r, int pixel_count, Stencil* stencil) {
	uint16_t palette_count;
	if (!read_u16(r, &palette_count)) return NULL;
	if (palette_count == 0 || palette_count > 256) return NULL;

	uint32_t* palette = (uint32_t*)apf_malloc_array((size_t)palette_count, sizeof(uint32_t));
	if (!palette) return NULL;
	for (int i = 0; i < palette_count; i++) {
		int32_t argb;
		if (!read_i32(r, &argb)) { free(palette); return NULL; }
		palette[i] = argb_to_rgba(argb);
	}

	uint8_t bits_per_index;
	if (!read_u8(r, &bits_per_index)) { free(palette); return NULL; }
	if (!(bits_per_index == 1 || bits_per_index == 2 || bits_per_index == 4 || bits_per_index == 8)) {
		free(palette);
		return NULL;
	}

	int packed_len;
	if (bits_per_index == 8) {
		packed_len = pixel_count;
	}
	else {
		int bit_count;
		if (!checked_mul_int(pixel_count, bits_per_index, &bit_count) ||
			!checked_byte_count_for_bits(bit_count, &packed_len)) {
			free(palette);
			return NULL;
		}
	}

	/* Read compressed delta packed indices */
	uint8_t* delta = read_compressed(r, packed_len);
	if (!delta) { free(palette); return NULL; }
	delta_decode_inplace(delta, packed_len);

	uint8_t* indices;
	if (bits_per_index == 8) {
		indices = delta;
	}
	else {
		indices = unpack_bits(delta, bits_per_index, pixel_count);
		free(delta);
		if (!indices) { free(palette); return NULL; }
	}

	/* Build z-ordered RGBA pixels */
	uint32_t* z_pixels = (uint32_t*)apf_malloc_array((size_t)pixel_count, sizeof(uint32_t));
	if (!z_pixels) { free(indices); free(palette); return NULL; }
	for (int i = 0; i < pixel_count; i++) {
		if (indices[i] >= palette_count) { free(z_pixels); free(indices); free(palette); return NULL; }
		z_pixels[i] = palette[indices[i]];
	}

	free(indices);
	free(palette);

	int total;
	if (!checked_total_pixels(stencil->width, stencil->height, &total)) { free(z_pixels); return NULL; }
	int* z_order = generate_z_order_indices(stencil->width, stencil->height);
	uint32_t* result = reorder_from_z_order(z_order, z_pixels, pixel_count, stencil->bits, total);
	free(z_order);
	free(z_pixels);
	return result;
}

/* ---------- Decode Mode 2: ColorSorted ---------- */

static uint32_t* decode_color_sorted(Reader* r, int pixel_count) {
	int32_t unique_count;
	if (!read_i32(r, &unique_count)) return NULL;
	if (!checked_count(unique_count, pixel_count)) return NULL;

	int32_t* colors = (int32_t*)apf_malloc_array((size_t)unique_count, sizeof(int32_t));
	int32_t* counts = (int32_t*)apf_malloc_array((size_t)unique_count, sizeof(int32_t));
	if (!colors || !counts) { free(colors); free(counts); return NULL; }

	for (int i = 0; i < unique_count; i++)
		if (!read_i32(r, &colors[i])) { free(colors); free(counts); return NULL; }
	long long total_count = 0;
	for (int i = 0; i < unique_count; i++) {
		if (!read_i32(r, &counts[i])) { free(colors); free(counts); return NULL; }
		if (!checked_count(counts[i], pixel_count)) { free(colors); free(counts); return NULL; }
		total_count += counts[i];
	}
	if (total_count != pixel_count) { free(colors); free(counts); return NULL; }

	uint8_t pos_width;
	if (!read_u8(r, &pos_width)) { free(colors); free(counts); return NULL; }
	if (!(pos_width == 1 || pos_width == 2 || pos_width == 4)) { free(colors); free(counts); return NULL; }

	int pos_bytes_len;
	if (!checked_mul_int(pixel_count, pos_width, &pos_bytes_len)) { free(colors); free(counts); return NULL; }
	uint8_t* pos_bytes = read_compressed(r, pos_bytes_len);
	if (!pos_bytes) { free(colors); free(counts); return NULL; }

	int* pos_deltas = bytes_to_ints(pos_bytes, pos_width, pixel_count);
	free(pos_bytes);
	if (!pos_deltas) { free(colors); free(counts); return NULL; }

	uint32_t* image_data = (uint32_t*)apf_calloc_array((size_t)pixel_count, sizeof(uint32_t));
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
			if (pos < 0 || pos >= pixel_count) {
				free(image_data); free(colors); free(counts); free(pos_deltas); return NULL;
			}
			image_data[pos] = rgba;
		}
	}
	if (di != pixel_count) { free(image_data); free(colors); free(counts); free(pos_deltas); return NULL; }

	free(colors);
	free(counts);
	free(pos_deltas);
	return image_data;
}

/* ---------- Decode Mode 3: SolidFill ---------- */

static uint32_t* decode_solid_fill(Reader* r, int pixel_count) {
	int32_t argb;
	if (!read_i32(r, &argb)) return NULL;
	uint32_t rgba = argb_to_rgba(argb);
	uint32_t* data = (uint32_t*)apf_malloc_array((size_t)pixel_count, sizeof(uint32_t));
	if (!data) return NULL;
	for (int i = 0; i < pixel_count; i++)
		data[i] = rgba;
	return data;
}

/* ---------- Decode Mode 4: MonoAlpha ---------- */

static uint32_t* decode_mono_alpha(Reader* r, int pixel_count, Stencil* stencil) {
	uint8_t* luma = read_compressed_plane(r, pixel_count);
	if (!luma) return NULL;

	uint8_t has_alpha;
	if (!read_u8(r, &has_alpha)) { free(luma); return NULL; }

	uint8_t* alpha;
	if (has_alpha) {
		alpha = read_compressed_plane(r, pixel_count);
	}
	else {
		uint8_t const_alpha;
		if (!read_u8(r, &const_alpha)) { free(luma); return NULL; }
		alpha = fill_plane(pixel_count, const_alpha);
	}
	if (!alpha) { free(luma); return NULL; }

	uint32_t* z_pixels = (uint32_t*)apf_malloc_array((size_t)pixel_count, sizeof(uint32_t));
	if (!z_pixels) { free(luma); free(alpha); return NULL; }
	for (int i = 0; i < pixel_count; i++) {
		uint8_t l = luma[i];
		z_pixels[i] = (uint32_t)l | ((uint32_t)l << 8) | ((uint32_t)l << 16) | ((uint32_t)alpha[i] << 24);
	}
	free(luma);
	free(alpha);

	int total;
	if (!checked_total_pixels(stencil->width, stencil->height, &total)) { free(z_pixels); return NULL; }
	int* z_order = generate_z_order_indices(stencil->width, stencil->height);
	uint32_t* result = reorder_from_z_order(z_order, z_pixels, pixel_count, stencil->bits, total);
	free(z_order);
	free(z_pixels);
	return result;
}

/* ---------- Decode Mode 5: PaethFullGrid ---------- */

static uint32_t* decode_paeth_full_grid(Reader* r, int pixel_count, Stencil* stencil) {
	int w = stencil->width, h = stencil->height;
	int total;
	if (!checked_total_pixels(w, h, &total)) return NULL;

	uint8_t channel_flags;
	if (!read_u8(r, &channel_flags)) return NULL;
	int has_r = channel_flags & 1;
	int has_g = channel_flags & 2;
	int has_b = channel_flags & 4;
	int has_a = channel_flags & 8;
	int is_mono = channel_flags & 16;

	uint8_t* r_plane = NULL, * g_plane = NULL, * b_plane = NULL, * a_plane = NULL;
	uint8_t val;

	if (has_r)
		r_plane = read_paeth_plane(r, w, h);
	else {
		if (!read_u8(r, &val)) return NULL;
		r_plane = fill_plane(total, val);
	}
	if (!r_plane) return NULL;

	if (is_mono) {
		g_plane = r_plane;
		b_plane = r_plane;
	}
	else {
		if (has_g)
			g_plane = read_paeth_plane(r, w, h);
		else {
			if (!read_u8(r, &val)) { free(r_plane); return NULL; }
			g_plane = fill_plane(total, val);
		}
		if (has_b)
			b_plane = read_paeth_plane(r, w, h);
		else {
			if (!read_u8(r, &val)) { free(r_plane); free(g_plane); return NULL; }
			b_plane = fill_plane(total, val);
		}
	}

	if (has_a)
		a_plane = read_paeth_plane(r, w, h);
	else {
		if (!read_u8(r, &val)) goto paeth_fail;
		a_plane = fill_plane(total, val);
	}
	if (!a_plane) goto paeth_fail;

	{
		uint32_t* image_data = (uint32_t*)apf_malloc_array((size_t)pixel_count, sizeof(uint32_t));
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
		if (idx != pixel_count) { free(image_data); goto paeth_fail; }

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

/* ---------- Decode Mode 6: PaethChannelPlanes ---------- */

/* Decompress stencil-true residuals, then Paeth-decode using background at stencil-false positions */
static uint8_t* read_paeth_channel_plane(Reader* r, int w, int h, int pixel_count,
	uint8_t bg_val, const Stencil* stencil) {
	uint8_t* stencil_residuals = read_compressed(r, pixel_count);
	if (!stencil_residuals) return NULL;

	int total;
	if (!checked_total_pixels(w, h, &total)) { free(stencil_residuals); return NULL; }
	uint8_t* result = (uint8_t*)apf_malloc_array((size_t)total, 1);
	if (!result) { free(stencil_residuals); return NULL; }

	int si = 0;
	for (int y = 0; y < h; y++) {
		for (int x = 0; x < w; x++) {
			int i = y * w + x;
			uint8_t a = x > 0 ? result[i - 1] : 0;
			uint8_t b = y > 0 ? result[i - w] : 0;
			uint8_t c = (x > 0 && y > 0) ? result[i - w - 1] : 0;
			uint8_t predicted = paeth_predict(a, b, c);

			if (stencil_get(stencil, x, y))
				result[i] = (uint8_t)(stencil_residuals[si++] + predicted);
			else
				result[i] = bg_val;
		}
	}

	if (si != pixel_count) { free(result); free(stencil_residuals); return NULL; }
	free(stencil_residuals);
	return result;
}

static uint32_t* decode_paeth_channel_planes(Reader* r, int pixel_count, Stencil* stencil,
	int32_t bg_argb) {
	int w = stencil->width, h = stencil->height;
	int total;
	if (!checked_total_pixels(w, h, &total)) return NULL;

	uint8_t bg_r = (uint8_t)((bg_argb >> 16) & 0xFF);
	uint8_t bg_g = (uint8_t)((bg_argb >> 8) & 0xFF);
	uint8_t bg_b = (uint8_t)(bg_argb & 0xFF);
	uint8_t bg_a = (uint8_t)((bg_argb >> 24) & 0xFF);

	uint8_t channel_flags;
	if (!read_u8(r, &channel_flags)) return NULL;
	int has_r = channel_flags & 1;
	int has_g = channel_flags & 2;
	int has_b = channel_flags & 4;
	int has_a = channel_flags & 8;
	int is_mono = channel_flags & 16;

	uint8_t* r_plane = NULL, * g_plane = NULL, * b_plane = NULL, * a_plane = NULL;
	uint8_t val;

	if (has_r)
		r_plane = read_paeth_channel_plane(r, w, h, pixel_count, bg_r, stencil);
	else {
		if (!read_u8(r, &val)) return NULL;
		r_plane = fill_plane(total, val);
	}
	if (!r_plane) return NULL;

	if (is_mono) {
		g_plane = r_plane;
		b_plane = r_plane;
	}
	else {
		if (has_g)
			g_plane = read_paeth_channel_plane(r, w, h, pixel_count, bg_g, stencil);
		else {
			if (!read_u8(r, &val)) { free(r_plane); return NULL; }
			g_plane = fill_plane(total, val);
		}
		if (has_b)
			b_plane = read_paeth_channel_plane(r, w, h, pixel_count, bg_b, stencil);
		else {
			if (!read_u8(r, &val)) { free(r_plane); free(g_plane); return NULL; }
			b_plane = fill_plane(total, val);
		}
	}

	if (has_a)
		a_plane = read_paeth_channel_plane(r, w, h, pixel_count, bg_a, stencil);
	else {
		if (!read_u8(r, &val)) goto pcp_fail;
		a_plane = fill_plane(total, val);
	}
	if (!a_plane) goto pcp_fail;

	{
		uint32_t* image_data = (uint32_t*)apf_malloc_array((size_t)pixel_count, sizeof(uint32_t));
		if (!image_data) goto pcp_fail;

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
		if (idx != pixel_count) { free(image_data); goto pcp_fail; }

		if (!is_mono) { free(g_plane); free(b_plane); }
		free(r_plane);
		free(a_plane);
		return image_data;
	}

pcp_fail:
	if (!is_mono) { free(g_plane); free(b_plane); }
	free(r_plane);
	free(a_plane);
	return NULL;
}

/* ---------- String / metadata helpers ---------- */

static char* read_string(Reader* r) {
	int32_t len;
	if (!read_i32(r, &len) || len < 0 || len > APF_MAX_STRING_BYTES) return NULL;
	char* s = (char*)apf_malloc_array((size_t)len + 1, 1);
	if (!s) return NULL;
	if (len > 0 && !read_bytes(r, (uint8_t*)s, len)) { free(s); return NULL; }
	s[len] = '\0';
	return s;
}

static int read_metadata(Reader* r, ApfMetadata* meta) {
	int32_t count;
	if (!read_i32(r, &count) || !checked_count(count, APF_MAX_METADATA_ENTRIES)) return 0;
	meta->count = 0;
	meta->entries = NULL;
	if (count == 0) return 1;

	meta->entries = (ApfMetadataEntry*)apf_calloc_array((size_t)count, sizeof(ApfMetadataEntry));
	if (!meta->entries) return 0;
	meta->count = count;

	for (int i = 0; i < count; i++) {
		meta->entries[i].key = read_string(r);
		meta->entries[i].value = read_string(r);
		if (!meta->entries[i].key || !meta->entries[i].value) return 0;
	}
	return 1;
}

static void free_metadata(ApfMetadata* meta) {
	for (int i = 0; i < meta->count; i++) {
		free(meta->entries[i].key);
		free(meta->entries[i].value);
	}
	free(meta->entries);
	meta->entries = NULL;
	meta->count = 0;
}

/* ---------- Decode single-image payload (stencil + bg + pixels + mode) ---------- */

static int decode_payload(Reader* r, ApfImage* img) {
	Stencil stencil = { 0 };
	if (!decode_stencil(r, &stencil)) return 0;

	int32_t bg_argb, pixel_count;
	uint8_t mode;
	if (!read_i32(r, &bg_argb) || !read_i32(r, &pixel_count) || !read_u8(r, &mode)) {
		free(stencil.bits);
		return 0;
	}
	int total;
	int shape_count = stencil_count(&stencil);
	if (!checked_total_pixels(stencil.width, stencil.height, &total) ||
		!checked_count(pixel_count, total) ||
		shape_count < 0 ||
		pixel_count != shape_count) {
		free(stencil.bits);
		return 0;
	}
	uint32_t bg_rgba = argb_to_rgba(bg_argb);

	uint32_t* image_data = NULL;
	switch (mode) {
	case 0: image_data = decode_channel_planes(r, pixel_count, &stencil); break;
	case 1: image_data = decode_palette_indexed(r, pixel_count, &stencil); break;
	case 2: image_data = decode_color_sorted(r, pixel_count); break;
	case 3: image_data = decode_solid_fill(r, pixel_count); break;
	case 4: image_data = decode_mono_alpha(r, pixel_count, &stencil); break;
	case 5: image_data = decode_paeth_full_grid(r, pixel_count, &stencil); break;
	case 6: image_data = decode_paeth_channel_planes(r, pixel_count, &stencil, bg_argb); break;
	default:
		fprintf(stderr, "apf: unknown encoding mode %d\n", mode);
		break;
	}

	if (!image_data) { free(stencil.bits); return 0; }

	int w = stencil.width, h = stencil.height;
	uint32_t* pixels = (uint32_t*)apf_malloc_array((size_t)total, sizeof(uint32_t));
	if (!pixels) { free(image_data); free(stencil.bits); return 0; }

	int img_idx = 0;
	for (int i = 0; i < total; i++) {
		if ((stencil.bits[i / 8] >> (i % 8)) & 1)
			pixels[i] = image_data[img_idx++];
		else
			pixels[i] = bg_rgba;
	}

	free(image_data);
	free(stencil.bits);

	img->width = w;
	img->height = h;
	img->pixels = pixels;
	return 1;
}

/* ---------- Public API ---------- */

static int strcasecmp_portable(const char* a, const char* b) {
	while (*a && *b) {
		char ca = *a >= 'A' && *a <= 'Z' ? *a + 32 : *a;
		char cb = *b >= 'A' && *b <= 'Z' ? *b + 32 : *b;
		if (ca != cb) return ca - cb;
		a++; b++;
	}
	return (unsigned char)*a - (unsigned char)*b;
}

static ApfFile* apf_load_reader(Reader* reader) {
	uint8_t version;
	if (!read_u8(reader, &version)) return NULL;

	ApfFile* apf = (ApfFile*)apf_calloc_array(1, sizeof(ApfFile));
	if (!apf) return NULL;
	apf->version = version;

	switch (version) {
	case 0x10: { /* v1.0: single image, no metadata */
		apf->image_count = 1;
		apf->images = (ApfImage*)apf_calloc_array(1, sizeof(ApfImage));
		if (!apf->images) goto fail;
		apf->images[0].name = apf_strdup("");
		if (!apf->images[0].name) goto fail;
		if (!decode_payload(reader, &apf->images[0])) goto fail;
		break;
	}

	case 0x11: { /* v1.1: single image with metadata */
		apf->image_count = 1;
		apf->images = (ApfImage*)apf_calloc_array(1, sizeof(ApfImage));
		if (!apf->images) goto fail;
		apf->images[0].name = apf_strdup("");
		if (!apf->images[0].name) goto fail;
		if (!read_metadata(reader, &apf->images[0].metadata)) goto fail;
		if (!decode_payload(reader, &apf->images[0])) goto fail;
		break;
	}

	case 0x20: { /* v2.0: multi-image container */
		int32_t image_count;
		if (!read_i32(reader, &image_count) || !checked_count(image_count, APF_MAX_IMAGES)) goto fail;
		if (image_count == 0) {
			fprintf(stderr, "apf: file contains no images\n");
			goto fail;
		}

		apf->image_count = image_count;
		apf->images = (ApfImage*)apf_calloc_array((size_t)image_count, sizeof(ApfImage));
		if (!apf->images) goto fail;

		for (int i = 0; i < image_count; i++) {
			apf->images[i].name = read_string(reader);
			if (!apf->images[i].name) goto fail;

			uint8_t sub_version;
			if (!read_u8(reader, &sub_version)) goto fail;

			if (sub_version == 0x11) {
				if (!read_metadata(reader, &apf->images[i].metadata)) goto fail;
			}
			else if (sub_version != 0x10) {
				fprintf(stderr, "apf: unknown sub-version 0x%02X\n", sub_version);
				goto fail;
			}

			if (!decode_payload(reader, &apf->images[i])) goto fail;
		}
		break;
	}

	default:
		fprintf(stderr, "apf: unknown version 0x%02X\n", version);
		goto fail;
	}

	return apf;

fail:
	apf_free_file(apf);
	return NULL;
}

ApfFile* apf_load_memory(const uint8_t* data, size_t size) {
	Reader reader;
	if (size > (size_t)APF_MAX_BYTES) return NULL;
	if (size > 0 && !data) return NULL;

	reader.data = data;
	reader.size = size;
	reader.pos = 0;
	return apf_load_reader(&reader);
}

ApfFile* apf_load_file(const char* path) {
	FILE* f = fopen(path, "rb");
	uint8_t* file_data;
	ApfFile* apf;
	long file_size;
	if (!f) return NULL;

	if (fseek(f, 0, SEEK_END) != 0) { fclose(f); return NULL; }
	file_size = ftell(f);
	if (file_size < 0 || file_size > APF_MAX_BYTES) { fclose(f); return NULL; }
	if (fseek(f, 0, SEEK_SET) != 0) { fclose(f); return NULL; }

	file_data = (uint8_t*)apf_malloc_array((size_t)file_size, 1);
	if (!file_data) { fclose(f); return NULL; }
	if ((long)fread(file_data, 1, file_size, f) != file_size) {
		free(file_data);
		fclose(f);
		return NULL;
	}
	fclose(f);

	apf = apf_load_memory(file_data, (size_t)file_size);
	free(file_data);
	return apf;
}

ApfImage* apf_file_get_image(ApfFile* file, const char* name) {
	if (!file || file->image_count == 0) return NULL;
	if (!name || name[0] == '\0') return &file->images[0];

	for (int i = 0; i < file->image_count; i++) {
		if (file->images[i].name && strcasecmp_portable(file->images[i].name, name) == 0)
			return &file->images[i];
	}
	return &file->images[0];
}

void apf_free_file(ApfFile* file) {
	if (!file) return;
	for (int i = 0; i < file->image_count; i++) {
		free(file->images[i].pixels);
		free(file->images[i].name);
		free_metadata(&file->images[i].metadata);
	}
	free(file->images);
	free(file);
}
