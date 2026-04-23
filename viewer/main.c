#include "apf.h"
#include "raylib.h"
#include <stdio.h>
#include <string.h>

int main(int argc, char* argv[]) {
	const char* apf_path = NULL;

	if (argc >= 2) {
		apf_path = argv[1];
	}
	else {
		fprintf(stderr, "Usage: apf_viewer <file.apf>\n");
		return 1;
	}

	ApfFile* file = apf_load_file(apf_path);
	if (!file) {
		fprintf(stderr, "Failed to load: %s\n", apf_path);
		return 1;
	}

	printf("Loaded %s (v%d.%d, %d image%s)\n", apf_path,
		file->version >> 4, file->version & 0xF,
		file->image_count, file->image_count != 1 ? "s" : "");

	for (int i = 0; i < file->image_count; i++) {
		ApfImage* img = &file->images[i];
		printf("  [%d] \"%s\" %dx%d", i, img->name, img->width, img->height);
		if (img->metadata.count > 0) {
			printf(" (%d metadata)\n", img->metadata.count);
			for (int m = 0; m < img->metadata.count; m++)
				printf("       %s = %s\n", img->metadata.entries[m].key, img->metadata.entries[m].value);
		}
		else {
			printf("\n");
		}
	}

	int current = 0;
	ApfImage* img = &file->images[current];

	int win_w = img->width;
	int win_h = img->height;
	if (win_w < 320) win_w = 320;
	if (win_h < 240) win_h = 240;

	char title[256];
	snprintf(title, sizeof(title), "APF Viewer - %s [%d/%d]",
		img->name[0] ? img->name : apf_path, current + 1, file->image_count);

	InitWindow(win_w, win_h, title);
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
		int changed = 0;
		if (file->image_count > 1) {
			if (IsKeyPressed(KEY_RIGHT) || IsKeyPressed(KEY_DOWN)) {
				current = (current + 1) % file->image_count;
				changed = 1;
			}
			if (IsKeyPressed(KEY_LEFT) || IsKeyPressed(KEY_UP)) {
				current = (current - 1 + file->image_count) % file->image_count;
				changed = 1;
			}
		}

		if (changed) {
			UnloadTexture(texture);
			img = &file->images[current];

			snprintf(title, sizeof(title), "APF Viewer - %s [%d/%d]",
				img->name[0] ? img->name : apf_path, current + 1, file->image_count);
			SetWindowTitle(title);

			int new_w = img->width < 320 ? 320 : img->width;
			int new_h = img->height < 240 ? 240 : img->height;
			SetWindowSize(new_w, new_h);

			ray_img.data = img->pixels;
			ray_img.width = img->width;
			ray_img.height = img->height;
			texture = LoadTextureFromImage(ray_img);
		}

		BeginDrawing();
		ClearBackground(DARKGRAY);

		int x = (GetScreenWidth() - img->width) / 2;
		int y = (GetScreenHeight() - img->height) / 2;
		DrawTexture(texture, x, y, WHITE);

		if (file->image_count > 1) {
			DrawText(TextFormat("[%d/%d] %s", current + 1, file->image_count,
				img->name[0] ? img->name : ""), 10, 10, 20, RAYWHITE);
		}

		EndDrawing();
	}

	UnloadTexture(texture);
	CloseWindow();
	apf_free_file(file);
	return 0;
}
