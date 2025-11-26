#include "ClipboardManager.h"
#include <chrono>
#include <thread>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <fstream>
#include <vector>
#include <Windows.h>
#include <bcrypt.h>

#pragma comment (lib, "bcrypt.lib")

ClipboardManager::ClipboardManager(
	const std::string& dbPath,
	const std::string& username,
	const std::string& workstation)
	: db(dbPath), username(username), workstation(workstation)
{
	ensure_schema();
}

ClipboardManager::ClipboardManager(const sqlite::database& externalDb) : db(externalDb), username("TestUser"), workstation("TestWorkstation")
{
	ensure_schema();
}

void ClipboardManager::ensure_schema()
{

	db << "CREATE TABLE IF NOT EXISTS clip ("
		"id INTEGER PRIMARY KEY AUTOINCREMENT, "
		"data TEXT COLLATE NOCASE, "
		"image_path TEXT, "
		"username TEXT COLLATE NOCASE, "
		"workstation TEXT COLLATE NOCASE, "
		"week TEXT, "
		"timestamp DATETIME DEFAULT CURRENT_TIMESTAMP, "
		"content_hash TEXT UNIQUE"
		");";

	db << "CREATE TABLE IF NOT EXISTS imports ("
		"id INTEGER PRIMARY KEY AUTOINCREMENT, "
		"name TEXT, "
		"imported_at TEXT, "
		"imported_by TEXT, "
		"path TEXT, "
		"entry_count INTEGER, "
		"workstation TEXT"
		");";
}

std::string ClipboardManager::sha256_hex_string(const std::string& input) const
{
	BCRYPT_ALG_HANDLE hAlg = nullptr;
	BCRYPT_HANDLE hHash = nullptr;
	DWORD cbHash = 0, cbData = 0;

	if (BCryptOpenAlgorithmProvider(&hAlg, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0)
		return {};

	if (BCryptGetProperty(hAlg, BCRYPT_HASH_LENGTH, (PUCHAR)&cbHash, sizeof(DWORD), &cbData, 0) < 0)
	{
		BCryptCloseAlgorithmProvider(hAlg, 0);
		return {};
	}

	std::vector<BYTE> hash(cbHash);
	if (BCryptCreateHash(hAlg, &hHash, nullptr, 0, nullptr, 0, 0) < 0)
	{
		BCryptCloseAlgorithmProvider(hAlg, 0);
		return {};
	}

	BCryptHashData(hHash, (PUCHAR)input.data(), (ULONG)input.size(), 0);
	BCryptFinishHash(hHash, hash.data(), cbHash, 0);

	// convert to uppercase hex string
	std::ostringstream oss;
	for (BYTE b : hash)
		oss << std::uppercase << std::hex << std::setw(2) << std::setfill('0') << (int)(b);
	std::string hex = oss.str();

	BCryptDestroyHash(hHash);
	BCryptCloseAlgorithmProvider(hAlg, 0);
	return hex;
}

std::string ClipboardManager::compute_content_hash_string(
	const std::string& data,
	const std::string& imagePath,
	const std::string& timestamp) const
{
	std::string hash_input;
	hash_input.append(data).append("|").append(imagePath).append("|").append(timestamp);
	auto hash = sha256_hex_string(hash_input);
	return hash;
}

std::string ClipboardManager::compute_file_hash_string(const std::string& path) const
{
	std::ifstream file(path, std::ios::binary);
	if (!file) return {};
	std::hash<std::string> hasher;
	std::string data((std::istreambuf_iterator<char>(file)), {});
	auto h = hasher(data);
	return std::to_string(h);
}

tm ClipboardManager::get_local_time()
{
	auto now = std::chrono::system_clock::now();
	auto t = std::chrono::system_clock::to_time_t(now);
	tm local_tm{};
	localtime_s(&local_tm, &t);
	return local_tm;
}

std::string ClipboardManager::timestamp_string(const tm& time) const
{
	char timestamp_buf[32];
	strftime(timestamp_buf, sizeof(timestamp_buf), "%Y-%m-%d %H:%M:%S", &time);
	return timestamp_buf;
}

std::string ClipboardManager::week_string(const tm& time) const
{
	char week_buf[16];
	sprintf_s(week_buf, "%04d-W%02d", 1900 + time.tm_year, (time.tm_yday / 7) + 1);
	return week_buf;
}

void ClipboardManager::save_clipboard_entry(
	const std::string& text,
	const std::string& imagePath,
	const std::string& time)
{
	bool valid_time = false;
	tm time_buf{};
	if (!time.empty())
	{
		std::istringstream ss(time);
		ss >> std::get_time(&time_buf, "%Y-%m-%d %H:%M:%S");
		if (!ss.fail()) valid_time = true;
	}
	if (!valid_time) time_buf = get_local_time();
	auto week = week_string(time_buf);
	auto timestamp = timestamp_string(time_buf);
	auto hash = compute_content_hash_string(text, imagePath, timestamp);
	db << "INSERT INTO clip (data, image_path, username, workstation, week, timestamp, content_hash) VALUES (?, ?, ?, ?, ?, ?, ?);"
		<< text << imagePath << username << workstation << week << timestamp << hash;
}