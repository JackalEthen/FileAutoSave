# 快照存全量二进制，文本投影只用于对比

_Status: accepted_

曾考虑把 Word 快照存成 `.txt`、Excel 快照存成 `.csv`，以便像 git 一样做增量存储。否决：Word→txt 会丢格式/图片/表格/修订，Excel→csv 会丢公式/多 sheet/图表/宏——**回滚时会毁掉文档**，不能作为恢复源。且 Office 文档是 OOXML 二进制 zip，无法做文本增量补丁重建。

决定：**快照 = 全量二进制副本**，靠内容哈希去重（内容未变则不存）与保留策略抽稀来控制体积；另为每个快照生成**文本投影**（Word→txt，Excel→规范化 TSV，保留公式与多 sheet），**仅用于版本历史的差异展示**，可随时重建、可安全丢失。回滚恢复的始终是对应时刻的二进制快照。

## Considered Options

- 文本/csv 当作增量主存储 —— 有损，否。
- OOXML 结构级 diff 再回拼 docx —— 可行但复杂（`word/document.xml` 的 rsid 每次编辑都变，需先清洗），收益不抵复杂度，暂不做。
