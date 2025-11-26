#include "pch.h"

#include "../../DbClip/ClipboardManager.h"

TEST(DbClipTests, WeekComputation)
{
	const sqlite::database db(":memory:");
	const ClipboardManager cm(db);

	tm test_time{};
	test_time.tm_year = 2025 - 1900;
	test_time.tm_mday = 1;
	const auto stamp = std::mktime(&test_time);
	EXPECT_NE(stamp, static_cast<std::time_t>(-1));
	EXPECT_EQ(cm.week_string(test_time), "2025-W01");
}

TEST(DbClipTests, ContentHashWorks)
{
	const auto dbPath = "test.db";
	const ClipboardManager cm(dbPath,  "TestUser", "TestWorkstation");

	const auto h1 = cm.compute_file_hash_string(dbPath);
	const auto h2 = cm.compute_file_hash_string(dbPath);

	EXPECT_EQ(h1, h2);
}

TEST(DbClipTests, HexSha256)
{
	const sqlite::database db(":memory:");
	const ClipboardManager cm(db);

	const auto hex = cm.sha256_hex_string("Hello");
	EXPECT_EQ(hex.size(), 64);
}

TEST(DbClipTests, SaveTextData_WritesRowToDb)
{
	sqlite::database db(":memory:");
	ClipboardManager cm(db);
	cm.save_clipboard_entry("test", "");
	int count = 0;
	db << "SELECT COUNT(*) FROM clip;" >> count;

	EXPECT_EQ(count, 1);
}


TEST(DbClipTests, SaveImageData_WritesRowToDb)
{
	sqlite::database db(":memory:");
	ClipboardManager cm(db);
	cm.save_clipboard_entry("", "test.png");
	int count = 0;
	db << "SELECT COUNT(*) FROM clip;" >> count;

	EXPECT_EQ(count, 1);
}

TEST(DbClipTests, SaveTextData_SkipsDuplicateViaContentHash)
{
	const sqlite::database db(":memory:");
	ClipboardManager cm(db);
	const auto time = cm.timestamp_string(ClipboardManager::get_local_time());
	cm.save_clipboard_entry("test", "", time);
	/*try
	{
		cm.save_clipboard_entry("test", "", time);
	}
	catch (const std::exception& ex)
	{
		std::cout << typeid(ex).name() << " : " << ex.what() << '\n';
	}*/
	EXPECT_THROW(
		cm.save_clipboard_entry("test", "", time),
		sqlite::errors::constraint
	);
}