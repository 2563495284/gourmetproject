import fs from "node:fs/promises";
import path from "node:path";
import { FileBlob, SpreadsheetFile } from "@oai/artifact-tool";

const root = "/Users/hcm-b0451/gourmetproject/GourmetProject";
const outputDir = path.join(root, "outputs/019ff3c9-remove-variant-rotation");
const before = await SpreadsheetFile.importXlsx(await FileBlob.load(path.join(root, "GameConfig/Datas/dish.xlsx")));
const after = await SpreadsheetFile.importXlsx(await FileBlob.load(path.join(outputDir, "dish.xlsx")));
const diffs = [];

const beforeSheets = before.worksheets.items;
const afterSheets = after.worksheets.items;
if (beforeSheets.length !== afterSheets.length) diffs.push("sheet count changed");

for (let i = 0; i < beforeSheets.length; i += 1) {
  const left = beforeSheets[i];
  const right = afterSheets[i];
  if (left.name !== right.name) {
    diffs.push(`sheet ${i} name changed`);
    continue;
  }
  if (left.name !== "dish_variant") {
    if (JSON.stringify(left.getUsedRange()?.values ?? []) !== JSON.stringify(right.getUsedRange()?.values ?? [])) {
      diffs.push(`${left.name}: values changed`);
    }
    if (JSON.stringify(left.getUsedRange()?.formulas ?? []) !== JSON.stringify(right.getUsedRange()?.formulas ?? [])) {
      diffs.push(`${left.name}: formulas changed`);
    }
    continue;
  }

  const leftValues = left.getRange("A1:L292").values;
  const rightValues = right.getRange("A1:K292").values;
  const leftFormulas = left.getRange("A1:L292").formulas;
  const rightFormulas = right.getRange("A1:K292").formulas;
  for (let row = 0; row < leftValues.length; row += 1) {
    const expectedValues = leftValues[row].slice(0, 10).concat(leftValues[row].slice(11));
    const expectedFormulas = leftFormulas[row].slice(0, 10).concat(leftFormulas[row].slice(11));
    if (JSON.stringify(expectedValues) !== JSON.stringify(rightValues[row])) {
      diffs.push(`dish_variant row ${row + 1}: values differ outside removed rotation column`);
    }
    if (JSON.stringify(expectedFormulas) !== JSON.stringify(rightFormulas[row])) {
      diffs.push(`dish_variant row ${row + 1}: formulas differ outside removed rotation column`);
    }
  }
}

const stale = await after.inspect({
  kind: "match",
  searchTerm: "rotation|DishRotation|Deg0|Deg90|Deg180|Deg270",
  options: { useRegex: true, maxResults: 100 },
  maxChars: 6000,
});
if (!stale.ndjson.includes("matched 0")) diffs.push("stale rotation text remains in dish workbook");

const finalDir = path.join(outputDir, "final_sheets");
await fs.mkdir(finalDir, { recursive: true });
for (const [index, sheet] of after.worksheets.items.entries()) {
  const preview = await after.render({ sheetName: sheet.name, autoCrop: "all", scale: 1, format: "png" });
  const safe = sheet.name.replace(/[^a-zA-Z0-9_-]+/g, "_");
  await fs.writeFile(path.join(finalDir, `${index}_${safe}.png`), new Uint8Array(await preview.arrayBuffer()));
}

console.log(JSON.stringify({ ok: diffs.length === 0, diffs: diffs.slice(0, 100) }, null, 2));
if (diffs.length > 0) process.exitCode = 1;
