#ifndef APF_H
#define APF_H

#include <stdint.h>

typedef struct {
    int width;
    int height;
    uint32_t *pixels; /* RGBA, row-major (width * height), background filled */
} ApfImage;

/* Load an APF v1.0 file. Returns NULL on failure. */
ApfImage *apf_load(const char *path);

/* Free an image returned by apf_load. */
void apf_free(ApfImage *img);

#endif /* APF_H */
