// ReSharper disable CppCStyleCast
// ReSharper disable CppUnreachableCode
#include "ClipboardManager.h"
#include <windows.h>
#include <gdiplus.h>
#include <filesystem>
#include <chrono>
#include <thread>
#include <sstream>

#pragma comment (lib, "gdiplus.lib")

enum save_type : uint8_t
{
	save_type_none = 0,
	save_type_text = 1,
	save_type_image = 2
};

namespace fs = std::filesystem;
using namespace Gdiplus;

namespace {
	bool load_encoder_clsid(const WCHAR* format, CLSID* p_clsid)
	{
		UINT num = 0; // number of image encoders
		UINT size = 0; // size, in bytes, of the image encoder array
		GetImageEncodersSize(&num, &size);
		if (size == 0) return false; // no encoders available

		std::vector<BYTE> buffer(size);
		auto* info = reinterpret_cast<ImageCodecInfo*>(buffer.data());
		GetImageEncoders(num, size, info);
		for (UINT i = 0; i < num; ++i)
		{
			if (wcscmp(info[i].MimeType, format) == 0)
			{
				*p_clsid = info[i].Clsid;
				return true;
			}
		}
		return false;
	}

	std::string clipboard_text_string()
	{
		std::string result;
		if (const HANDLE hData = GetClipboardData(CF_TEXT))
		{
			if (const auto psz_text = static_cast<char*>(GlobalLock(hData)))
			{
				result = psz_text;
				GlobalUnlock(hData);
			}
		}
		return result;
	}

	// saves clipboard bitmap as PNG, returns file path (or empty if none)
	std::string save_clipboard_image_by_week(const std::string& base_folder, const std::string& week)
	{
		HBITMAP hCopy = nullptr;

		if (IsClipboardFormatAvailable(CF_DIB))
		{
			// handle CF_DIB (Device Independent Bitmap)
			const auto hData = GetClipboardData(CF_DIB);
			if (!hData)
			{
				return {};
			}
			const auto bih = static_cast<BITMAPINFOHEADER*>(GlobalLock(hData));
			if (!bih)
			{
				return {};
			}

			const auto bits = reinterpret_cast<BYTE*>(bih) + bih->biSize + bih->biClrUsed * sizeof(RGBQUAD);
			const auto hdc = GetDC(nullptr);
			void* lp_bits = nullptr;

			hCopy = CreateDIBSection(hdc, reinterpret_cast<BITMAPINFO*>(bih), DIB_RGB_COLORS, &lp_bits, nullptr, 0);
			if (hCopy && lp_bits) memcpy(lp_bits, bits, bih->biSizeImage);
			ReleaseDC(nullptr, hdc);
			GlobalUnlock(hData);
		}
		else if (IsClipboardFormatAvailable(CF_BITMAP))
		{
			// handle CF_BITMAP
			if (const auto hClipboardBmp = static_cast<HBITMAP>(GetClipboardData(CF_BITMAP)))
			{
				// make a copy of the clipboard bitmap
				const auto hdc = GetDC(nullptr);
				const auto hdcMemSrc = CreateCompatibleDC(hdc);
				const auto hdcMemDest = CreateCompatibleDC(hdc);

				BITMAP bm;
				GetObject(hClipboardBmp, sizeof(bm), &bm);
				hCopy = CreateCompatibleBitmap(hdc, bm.bmWidth, bm.bmHeight);

				const auto oldSrc = SelectObject(hdcMemSrc, hClipboardBmp);
				const auto oldDest = SelectObject(hdcMemDest, hCopy);
				BitBlt(hdcMemDest, 0, 0, bm.bmWidth, bm.bmHeight, hdcMemSrc, 0, 0, SRCCOPY);
				SelectObject(hdcMemSrc, oldSrc);
				SelectObject(hdcMemDest, oldDest);

				DeleteDC(hdcMemSrc);
				DeleteDC(hdcMemDest);
				ReleaseDC(nullptr, hdc);
			}
		}

		if (!hCopy) return {};

		// generate output path, creating subfolder by week
		const auto folder = fs::path(base_folder) / week;
		fs::create_directories(folder);

		const auto timestamp = std::chrono::system_clock::now().time_since_epoch().count();
		const auto filename = folder / ("clip_" + std::to_string(timestamp) + ".png");

		{
			// create CGI+ bitmap from the copy
			Bitmap bmp(hCopy, nullptr);
			CLSID png_clsid;
			if (load_encoder_clsid(L"image/png", &png_clsid))
			{
				const auto wpath = filename.wstring();
				bmp.Save(wpath.c_str(), &png_clsid, nullptr);
			}
		}

		DeleteObject(hCopy);

		return filename.string();
	}

	std::string username_string()
	{
		char buffer[256];
		DWORD size = sizeof(buffer);
		return GetUserNameA(buffer, &size) ? buffer : "UnknownUser";
	}

	std::string workstation_string()
	{
		char buffer[256];
		DWORD size = sizeof(buffer);
		return GetComputerNameA(buffer, &size) ? buffer : "UnknownHost";
	}
}

int main()
{
	try
	{
		// initialize GDI+
		ULONG_PTR token;
		GdiplusStartupInput gdi_startup;
		GdiplusStartup(&token, &gdi_startup, nullptr);

		const std::string base_folder = "images";
		ClipboardManager cm("clipboard-history.db", username_string(), workstation_string());

		std::string prev_data;
		std::string prev_image_hash;

		std::cout << "Clipboard monitor started...\n";

		for (;;)
		{
			using namespace std::chrono_literals;
			std::this_thread::sleep_for(1s);

			if (!OpenClipboard(nullptr))
				continue;

			auto save_type = save_type_none;
			std::string data;
			std::string saved_path;
			std::string image_hash;

			// text
			if (IsClipboardFormatAvailable(CF_TEXT))
			{
				data = clipboard_text_string();
				if (!data.empty() && data != prev_data) save_type = save_type_text;
			}

			// image
			if (IsClipboardFormatAvailable(CF_DIB) || IsClipboardFormatAvailable(CF_BITMAP))
			{
				saved_path = save_clipboard_image_by_week(base_folder, cm.week_string(ClipboardManager::get_local_time()));
				if (!saved_path.empty())
				{
					image_hash = cm.compute_file_hash_string(saved_path);
					if (image_hash != prev_image_hash)
					{
						save_type = save_type_image;
					}
					else
					{
						// duplicate hash - remove redundant save
						fs::remove(saved_path);
					}
				}
			}

			CloseClipboard();

			try
			{
				switch (save_type)
				{
				case save_type_text:
					cm.save_clipboard_entry(data, "");
					std::cout << "New clipboard text saved (" << data.size() << " bytes)\n";
					prev_data = std::move(data);
					break;
				case save_type_image:
					cm.save_clipboard_entry("", saved_path);
					std::cout << "New clipboard image saved (" << saved_path << ")\n";
					prev_image_hash = std::move(image_hash);
					break;
				case save_type_none:
					break;
				}
			}
			catch (const sqlite::sqlite_exception& ex)
			{
				// duplicate hash, skip
				std::cerr << ex.what();
				save_type = save_type_none;
			}

			if (save_type == save_type_none)
			{
				std::this_thread::sleep_for(500ms);
			}
		}
		GdiplusShutdown(token);  // NOLINT(clang-diagnostic-unreachable-code)
		return 0;
	}
	catch (const std::exception& e)
	{
		std::cerr << "Error: " << e.what() << '\n';
		return 1;
	}
}