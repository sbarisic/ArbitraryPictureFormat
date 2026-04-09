#ifndef APF_H
#define APF_H

#include <stdint.h>

/* Key-value metadata entry */
typedef struct {
    char *key;
    char *value;
} ApfMetadataEntry;

/* Metadata dictionary */
typedef struct {
    ApfMetadataEntry *entries;
    int count;
} ApfMetadata;

/* Single decoded image with optional name and metadata */
typedef struct {
    int width;
    int height;
    uint32_t *pixels;       /* RGBA, row-major (width * height), background filled */
    char *name;             /* UTF-8 image name (empty string if unnamed) */
    ApfMetadata metadata;   /* key-value metadata (count=0 if none) */
} ApfImage;

/* Multi-image APF file container */
typedef struct {
    ApfImage *images;
    int image_count;
    uint8_t version;        /* 0x10, 0x11, or 0x20 */
} ApfFile;

/* Load an APF file (v1.0, v1.1, or v2.0). Returns NULL on failure. */
ApfFile *apf_load_file(const char *path);

/* Get image by name (case-insensitive). Returns first image if name is NULL/empty. */
ApfImage *apf_file_get_image(ApfFile *file, const char *name);

/* Free an entire ApfFile and all its images. */
void apf_free_file(ApfFile *file);

#endif /* APF_H */
