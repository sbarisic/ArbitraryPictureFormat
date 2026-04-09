#include "apf.h"
#include "raylib.h"
#include <stdio.h>
#include <string.h>

int main(int argc, char *argv[]) {
    const char *apf_path = NULL;

    if (argc >= 2) {
        apf_path = argv[1];
    } else {
        fprintf(stderr, "Usage: apf_viewer <file.apf>\n");
        return 1;
    }

    ApfImage *img = apf_load(apf_path);
    if (!img) {
        fprintf(stderr, "Failed to load: %s\n", apf_path);
        return 1;
    }

    printf("Loaded %s: %dx%d\n", apf_path, img->width, img->height);

    int win_w = img->width;
    int win_h = img->height;
    if (win_w < 320) win_w = 320;
    if (win_h < 240) win_h = 240;

    InitWindow(win_w, win_h, "APF Viewer");
    SetTargetFPS(60);

    Image ray_img = {
        .data = img->pixels,
        .width = img->width,
        .height = img->height,
        .mipmaps = 1,
        .format = PIXELFORMAT_UNCOMPRESSED_R8G8B8A8,
    };

    Texture2D texture = LoadTextureFromImage(ray_img);

    while (!WindowShouldClose()) {
        BeginDrawing();
        ClearBackground(DARKGRAY);

        int x = (win_w - img->width) / 2;
        int y = (win_h - img->height) / 2;
        DrawTexture(texture, x, y, WHITE);

        EndDrawing();
    }

    UnloadTexture(texture);
    CloseWindow();
    apf_free(img);
    return 0;
}
