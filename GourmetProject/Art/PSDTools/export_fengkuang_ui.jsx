#target photoshop

(function () {
    for (var openIndex = app.documents.length - 1; openIndex >= 0; openIndex--) {
        if (app.documents[openIndex].name === "References_battle_hud_reference.png") {
            app.documents[openIndex].close(SaveOptions.DONOTSAVECHANGES);
        }
    }

    var sourcePath = "/Users/yijin/Downloads/疯狂餐厅.psd";
    var projectRoot = "/Users/yijin/gourmetproject/GourmetProject";
    var assetRoot = projectRoot + "/Assets/GameMain/Content/Resources/Sprites/UI/FengKuangCanTing";
    var previewRoot = projectRoot + "/Assets/GameMain/Development/ArtPreviews/FengKuangCanTingPSD";

    var assets = [
        { file: "Backgrounds/bg_main_menu.png", paths: ["封面/图层 1", "封面/图层 2"], bounds: [0, 0, 1920, 1080] },
        { file: "Backgrounds/bg_battle.png", paths: ["图层 26"], bounds: [0, 0, 1920, 1080] },
        { file: "Branding/logo_game_title.png", paths: ["封面/组 1"] },
        { file: "Branding/menu_start_prompt.png", paths: ["封面/开始游戏"] },
        { file: "Branding/menu_label_settings.png", paths: ["封面/设置"] },
        { file: "Branding/menu_label_quit.png", paths: ["封面/退出"] },
        { file: "Icons/icon_menu_arrow_left.png", paths: ["封面/图层 4"] },
        { file: "Icons/icon_menu_arrow_right.png", paths: ["封面/图层 5"] },

        { file: "Panels/panel_dialog_large.png", paths: ["设置/二级窗口/矩形 1 拷贝"], border: [64, 64, 64, 64] },
        { file: "Panels/panel_dialog_header.png", paths: ["设置/二级窗口/矩形 1 拷贝", "设置/二级窗口/图层 6"], bounds: [334, 69, 1584, 172], border: [48, 24, 48, 24] },
        { file: "Controls/dropdown_field.png", paths: ["设置/分辨率/矩形 2"], border: [20, 18, 20, 18] },
        { file: "Icons/icon_dropdown_arrow.png", paths: ["设置/分辨率/图层 7"] },
        { file: "Controls/slider_track.png", paths: ["设置/音量拖动条/矩形 2"], border: [11, 9, 11, 9] },
        { file: "Controls/slider_fill.png", paths: ["设置/音量拖动条/矩形 2 拷贝 5"], border: [11, 9, 11, 9] },
        { file: "Controls/slider_handle.png", paths: ["设置/音量拖动条/椭圆 1"] },
        { file: "Controls/toggle_box.png", paths: ["设置/音量拖动条 拷贝 2/矩形 2 拷贝 6"], border: [7, 7, 7, 7] },
        { file: "Controls/toggle_check.png", paths: ["设置/音量拖动条 拷贝 2/图层 10"] },
        { file: "Controls/button_primary.png", paths: ["设置/按钮/矩形 1"], border: [28, 24, 28, 24] },
        { file: "Controls/button_secondary.png", paths: ["设置/按钮/矩形 1 拷贝 2"], border: [28, 24, 28, 24] },
        { file: "Controls/dropdown_popup.png", paths: ["设置/组 4/矩形 2 拷贝"], border: [20, 20, 20, 20] },
        { file: "Controls/scrollbar_track.png", paths: ["设置/组 4/矩形 2 拷贝 2"], border: [12, 12, 12, 12] },
        { file: "Controls/dropdown_hover.png", paths: ["设置/组 4/矩形 2 拷贝 4"], border: [16, 14, 16, 14] },
        { file: "Controls/scrollbar_thumb.png", paths: ["设置/组 4/矩形 2 拷贝 3"], border: [12, 12, 12, 12] },
        { file: "Panels/panel_dialog_confirm.png", paths: ["退出游戏/二级窗口/矩形 1 拷贝"], border: [48, 48, 48, 48] },

        { file: "Timeline/timeline_ribbon.png", paths: ["组 11/图层 13"] },
        { file: "Timeline/timeline_progress.png", paths: ["组 11/组 10/图层 23"], border: [8, 7, 8, 7] },
        { file: "Timeline/timeline_axis.png", paths: ["组 11/组 10/图层 21"], border: [8, 6, 8, 6] },
        { file: "Timeline/timeline_tick.png", paths: ["组 11/组 10/图层 22"] },
        { file: "Timeline/day_marker.png", paths: ["组 11/图层 24"], border: [24, 24, 24, 24] },

        { file: "Panels/panel_left_sidebar.png", paths: ["图层 12"] },
        { file: "Panels/card_week.png", paths: ["组 8/图层 15 拷贝 3"], border: [24, 20, 24, 20] },
        { file: "Panels/card_currency.png", paths: ["组 8/图层 17 拷贝"] },
        { file: "Icons/icon_coin.png", paths: ["组 8/金币"] },
        { file: "Icons/icon_heart_active.png", paths: ["组 8/图层 17"] },
        { file: "Icons/icon_heart_empty.png", paths: ["组 8/图层 17 拷贝 2"] },
        { file: "Panels/panel_score_section.png", paths: ["组 6/图层 15 拷贝"] },
        { file: "Panels/score_meter.png", paths: ["组 6/图层 18"] },
        { file: "Panels/score_title.png", paths: ["组 6/图层 15 拷贝 5"], border: [28, 20, 28, 20] },
        { file: "Panels/panel_stats_section.png", paths: ["组 7/图层 15 拷贝 7"] },
        { file: "Panels/panel_stats_composite.png", paths: ["组 7/图层 15 拷贝 7", "组 7/图层 17 拷贝 6"] },
        { file: "Panels/card_food_back.png", paths: ["组 5/图层 15 拷贝 4"] },
        { file: "Panels/card_food_front.png", paths: ["组 5/图层 15 拷贝 6"] },
        { file: "Panels/card_food_composite.png", paths: ["组 5/图层 15 拷贝 4", "组 5/图层 15 拷贝 6", "组 5/图层 17 拷贝 4"] },
        { file: "Panels/card_settings.png", paths: ["组 9/图层 15 拷贝 2"] },
        { file: "Icons/icon_settings_gear.png", paths: ["组 9/图层 20"] },
        { file: "Panels/card_playfield.png", paths: ["图层 12 拷贝 2"] },
        { file: "Panels/panel_right_sidebar.png", paths: ["图层 12 拷贝"] },
        { file: "Panels/header_passive_items.png", paths: ["图层 15 拷贝 10"], border: [24, 20, 24, 20] },
        { file: "Panels/panel_active_items.png", paths: ["图层 15 拷贝 8"] },
        { file: "Controls/active_item_card.png", paths: ["矩形 3 拷贝"], border: [20, 20, 20, 20] },
        { file: "Controls/active_item_card_stack.png", paths: ["矩形 3 拷贝 5", "矩形 3 拷贝 4", "矩形 3 拷贝 3", "矩形 3 拷贝 2", "矩形 3 拷贝"] },
        { file: "Panels/page_indicator.png", paths: ["图层 25"], border: [24, 20, 24, 20] },
        { file: "Panels/header_active_items.png", paths: ["图层 15 拷贝 9"], border: [24, 20, 24, 20] }
    ];

    var references = [
        { file: "References/main_menu_reference.png", paths: ["封面"], bounds: [0, 0, 1920, 1080] },
        {
            file: "References/battle_hud_reference.png",
            bounds: [0, 0, 1920, 1080],
            paths: [
                "图层 26", "组 11", "图层 12", "组 8", "组 6", "组 7", "组 5", "组 9",
                "图层 12 拷贝 2", "图层 12 拷贝 3", "图层 12 拷贝", "图层 15 拷贝 10",
                "被动道具", "图层 15 拷贝 8", "矩形 3 拷贝 5", "矩形 3 拷贝 4",
                "矩形 3 拷贝 3", "矩形 3 拷贝 2", "矩形 3 拷贝", "图层 25",
                "图层 15 拷贝 9", "主动道具"
            ]
        },
        {
            file: "References/settings_reference.png",
            bounds: [0, 0, 1920, 1080],
            paths: ["图层 26", "设置"]
        },
        {
            file: "References/quit_dialog_reference.png",
            bounds: [0, 0, 1920, 1080],
            paths: ["图层 26", "退出游戏"]
        }
    ];

    function ensureFolder(path) {
        var folder = new Folder(path);
        if (folder.exists) return folder;
        var parent = folder.parent;
        if (parent && !parent.exists) ensureFolder(parent.fsName);
        if (!folder.create()) throw new Error("Cannot create folder: " + path);
        return folder;
    }

    function findLayer(document, path) {
        for (var directIndex = 0; directIndex < document.layers.length; directIndex++) {
            if (document.layers[directIndex].name === path) return document.layers[directIndex];
        }
        var parts = path.split("/");
        var container = document;
        for (var p = 0; p < parts.length; p++) {
            var found = null;
            for (var i = 0; i < container.layers.length; i++) {
                if (container.layers[i].name === parts[p]) {
                    found = container.layers[i];
                    break;
                }
            }
            if (!found) throw new Error("Layer not found: " + path + " (missing " + parts[p] + ")");
            container = found;
        }
        return container;
    }

    function num(value) {
        return Math.round(value.as("px") * 1000) / 1000;
    }

    function layerBounds(layer) {
        var b = layer.bounds;
        return [num(b[0]), num(b[1]), num(b[2]), num(b[3])];
    }

    function unionBounds(document, paths) {
        var result = null;
        for (var i = 0; i < paths.length; i++) {
            var b = layerBounds(findLayer(document, paths[i]));
            if (!result) result = b;
            else {
                result[0] = Math.min(result[0], b[0]);
                result[1] = Math.min(result[1], b[1]);
                result[2] = Math.max(result[2], b[2]);
                result[3] = Math.max(result[3], b[3]);
            }
        }
        return result;
    }

    function exportPng(document, root, spec) {
        var bounds = spec.bounds ? spec.bounds : unionBounds(document, spec.paths);
        var width = Math.max(1, Math.ceil(bounds[2] - bounds[0]));
        var height = Math.max(1, Math.ceil(bounds[3] - bounds[1]));
        var output = new File(root + "/" + spec.file);
        ensureFolder(output.parent.fsName);
        if (output.exists) output.remove();

        var documentName = spec.file.replace(/[\\\/]/g, "_");
        var target = null;
        try {
            target = app.documents.add(width, height, 72, documentName, NewDocumentMode.RGB, DocumentFill.TRANSPARENT);
            app.activeDocument = document;
            for (var i = 0; i < spec.paths.length; i++) {
                var sourceLayer = findLayer(document, spec.paths[i]);
                var sourceBounds = layerBounds(sourceLayer);
                var copy = sourceLayer.duplicate(target, ElementPlacement.PLACEATBEGINNING);
                app.activeDocument = target;
                copy.visible = true;
                var copyBounds = layerBounds(copy);
                var desiredLeft = sourceBounds[0] - bounds[0];
                var desiredTop = sourceBounds[1] - bounds[1];
                copy.translate(UnitValue(desiredLeft - copyBounds[0], "px"), UnitValue(desiredTop - copyBounds[1], "px"));
                app.activeDocument = document;
            }

            app.activeDocument = target;
            var options = new ExportOptionsSaveForWeb();
            options.format = SaveDocumentType.PNG;
            options.PNG8 = false;
            options.transparency = true;
            options.interlaced = false;
            options.includeProfile = false;
            target.exportDocument(output, ExportType.SAVEFORWEB, options);

            var centerX = (bounds[0] + bounds[2]) / 2;
            var centerY = (bounds[1] + bounds[3]) / 2;
            return {
                file: spec.file,
                sourceLayers: spec.paths,
                sourceBounds: { left: bounds[0], top: bounds[1], right: bounds[2], bottom: bounds[3] },
                pixelSize: { width: width, height: height },
                unityReference: {
                    canvas: [1920, 1080],
                    pivot: [0.5, 0.5],
                    anchoredPositionFromCenter: [centerX - 960, 540 - centerY]
                },
                spriteBorder: spec.border ? spec.border : [0, 0, 0, 0]
            };
        } finally {
            if (target) {
                try { target.close(SaveOptions.DONOTSAVECHANGES); } catch (_) {}
            }
        }
    }

    function escapeJson(value) {
        return value.replace(/\\/g, "\\\\").replace(/\"/g, "\\\"").replace(/\r/g, "\\r").replace(/\n/g, "\\n");
    }

    function json(value, indent) {
        indent = indent || "";
        var next = indent + "  ";
        if (value === null) return "null";
        if (typeof value === "string") return "\"" + escapeJson(value) + "\"";
        if (typeof value === "number" || typeof value === "boolean") return String(value);
        if (value instanceof Array) {
            if (value.length === 0) return "[]";
            var arrayParts = [];
            for (var i = 0; i < value.length; i++) arrayParts.push(next + json(value[i], next));
            return "[\n" + arrayParts.join(",\n") + "\n" + indent + "]";
        }
        var parts = [];
        for (var key in value) {
            if (value.hasOwnProperty(key)) parts.push(next + json(key) + ": " + json(value[key], next));
        }
        return "{\n" + parts.join(",\n") + "\n" + indent + "}";
    }

    function writeText(path, content) {
        var file = new File(path);
        ensureFolder(file.parent.fsName);
        file.encoding = "UTF8";
        file.open("w");
        file.write(content);
        file.close();
    }

    var oldDialogs = app.displayDialogs;
    var oldUnits = app.preferences.rulerUnits;
    var sourceFile = new File(sourcePath);
    var document = null;
    var openedHere = false;
    var manifest = [];
    var log = [];

    try {
        app.displayDialogs = DialogModes.NO;
        app.preferences.rulerUnits = Units.PIXELS;
        if (!sourceFile.exists) throw new Error("PSD not found: " + sourcePath);
        document = app.open(sourceFile);
        openedHere = true;

        ensureFolder(assetRoot);
        ensureFolder(previewRoot);

        for (var i = 0; i < assets.length; i++) {
            try {
                manifest.push(exportPng(document, assetRoot, assets[i]));
                log.push("OK  " + assets[i].file);
            } catch (assetError) {
                log.push("ERR " + assets[i].file + ": " + assetError.message);
            }
        }

        for (var r = 0; r < references.length; r++) {
            try {
                exportPng(document, previewRoot, references[r]);
                log.push("OK  " + references[r].file);
            } catch (referenceError) {
                log.push("ERR " + references[r].file + ": " + referenceError.message);
            }
        }

        var manifestObject = {
            source: sourcePath,
            canvas: { width: 1920, height: 1080 },
            unity: { pixelsPerUnit: 100, textureType: "Sprite", spriteMode: "Single", mipmaps: false, wrapMode: "Clamp" },
            note: "PSD text layers remain editable in Unity. Only the stylized game logo is rasterized.",
            assets: manifest
        };
        writeText(previewRoot + "/manifest.json", json(manifestObject, "") + "\n");
        writeText(previewRoot + "/export_log.txt", log.join("\n") + "\n");
    } catch (error) {
        log.push("FATAL: " + error.message);
        try { writeText(previewRoot + "/export_log.txt", log.join("\n") + "\n"); } catch (_) {}
        throw error;
    } finally {
        if (openedHere && document) document.close(SaveOptions.DONOTSAVECHANGES);
        app.preferences.rulerUnits = oldUnits;
        app.displayDialogs = oldDialogs;
    }
}());
