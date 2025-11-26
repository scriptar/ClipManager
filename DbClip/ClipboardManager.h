#pragma once

#include <string>
#include "sqlite_modern_cpp.h"

class ClipboardManager
{
public:
	ClipboardManager(
		const std::string& dbPath,
		const std::string& username,
		const std::string& workstation);
	ClipboardManager(const sqlite::database& externalDb);
	std::string sha256_hex_string(const std::string& input) const;
	std::string compute_content_hash_string(
		const std::string& data,
		const std::string& imagePath,
		const std::string& timestamp) const;
	std::string compute_file_hash_string(const std::string& path) const;
	static tm get_local_time();
	std::string timestamp_string(const tm& time) const;
	std::string week_string(const tm& time) const;
	void save_clipboard_entry(const std::string& text, const std::string& imagePath, const std::string& time = "");

private:
	sqlite::database db;
	std::string base_folder;
	std::string username;
	std::string workstation;

	void ensure_schema();
};

