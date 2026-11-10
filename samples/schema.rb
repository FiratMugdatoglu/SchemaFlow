# This file is auto-generated from the current state of the database.
ActiveRecord::Schema[7.1].define(version: 2026_11_09_142000) do

  create_table "authors", force: :cascade do |t|
    t.string "name", null: false
    t.string "email", null: false
    t.datetime "created_at", null: false
    t.datetime "updated_at", null: false
    t.index ["email"], name: "index_authors_on_email", unique: true
  end

  create_table "posts", force: :cascade do |t|
    t.string "title", null: false
    t.text "content"
    t.bigint "author_id", null: false
    t.boolean "published", default: false
    t.datetime "created_at", null: false
    t.datetime "updated_at", null: false
    t.index ["author_id"], name: "index_posts_on_author_id"
  end

  add_foreign_key "posts", "authors", column: "author_id", on_delete: :cascade
end
