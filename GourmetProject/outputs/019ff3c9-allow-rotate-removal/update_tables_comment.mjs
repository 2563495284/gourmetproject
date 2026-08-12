import fs from "node:fs/promises";
import path from "node:path";
import { FileBlob, SpreadsheetFile } from "@oai/artifact-tool";

const root = "/Users/hcm-b0451/gourmetproject/GourmetProject";
const sourcePath = path.join(root, "GameConfig/Datas/__tables__.xlsx");
const outputDir = path.join(root, "outputs/019ff3c9-allow-rotate-removal");
const outputPath = path.join(outputDir, "__tables__.xlsx");
const comment = "食物本体：物理属性（id/name/deliciousness/shapeRows）与固有技能（skills→TbSkill）。";

const workbook = await SpreadsheetFile.importXlsx(await FileBlob.load(sourcePath));
const sheet = workbook.worksheets.getItem("Sheet1");
if (sheet.getRange("B4").values?.[0]?.[0] !== "TbDishBase") {
  throw new Error("Sheet1!B4 is not TbDishBase; refusing to edit table comment");
}
sheet.getRange("I4").values = [[comment]];

await fs.mkdir(outputDir, { recursive: true });
const output = await SpreadsheetFile.exportXlsx(workbook);
await output.save(outputPath);

const verificationWorkbook = await SpreadsheetFile.importXlsx(await FileBlob.load(outputPath));
const verification = await verificationWorkbook.inspect({
  kind: "table",
  range: "Sheet1!A1:K6",
  include: "values,formulas",
  tableMaxRows: 6,
  tableMaxCols: 11,
  maxChars: 8000,
});
console.log(verification.ndjson);

const stale = await verificationWorkbook.inspect({
  kind: "match",
  searchTerm: "allowRotate",
  options: { useRegex: false, maxResults: 20 },
  maxChars: 3000,
});
console.log(stale.ndjson);

const preview = await verificationWorkbook.render({
  sheetName: "Sheet1",
  autoCrop: "all",
  scale: 1,
  format: "png",
});
await fs.writeFile(
  path.join(outputDir, "after_tables.png"),
  new Uint8Array(await preview.arrayBuffer()),
);
console.log(JSON.stringify({ outputPath }));
