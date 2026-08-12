import fs from "node:fs/promises";
import path from "node:path";
import { FileBlob, SpreadsheetFile } from "@oai/artifact-tool";

const workDir = "/Users/hcm-b0451/gourmetproject/GourmetProject/outputs/019ff3c9-remove-variant-rotation";
const workbooks = [
  {
    label: "dish",
    source: "/Users/hcm-b0451/gourmetproject/GourmetProject/GameConfig/Datas/dish.xlsx",
  },
  {
    label: "enums_dish",
    source: "/Users/hcm-b0451/gourmetproject/GourmetProject/GameConfig/Datas/__enums_dish.xlsx",
  },
];

await fs.mkdir(path.join(workDir, "before"), { recursive: true });

for (const item of workbooks) {
  const workbook = await SpreadsheetFile.importXlsx(await FileBlob.load(item.source));
  const sheetInspection = await workbook.inspect({
    kind: "sheet",
    include: "id,name",
    maxChars: 12000,
  });
  const sheetNames = sheetInspection.ndjson
    .split("\n")
    .filter(Boolean)
    .map((line) => JSON.parse(line))
    .filter((record) => record.kind === "sheet")
    .map((record) => record.name);
  const inspection = await workbook.inspect({
    kind: "workbook,sheet,table",
    maxChars: 20000,
    tableMaxRows: 8,
    tableMaxCols: 14,
    tableMaxCellChars: 120,
  });
  await fs.writeFile(path.join(workDir, "before", `${item.label}.inspect.ndjson`), inspection.ndjson, "utf8");
  console.log(`=== ${item.label} ===`);
  console.log(sheetInspection.ndjson);
  for (const [sheetIndex, sheetName] of sheetNames.entries()) {
    const preview = await workbook.render({
      sheetName,
      autoCrop: "all",
      scale: 1,
      format: "png",
    });
    const safeName = sheetName.replace(/[^a-zA-Z0-9_-]+/g, "_");
    await fs.writeFile(
      path.join(workDir, "before", `${item.label}_${sheetIndex}_${safeName}.png`),
      new Uint8Array(await preview.arrayBuffer()),
    );
  }
}
