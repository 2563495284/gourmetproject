#target photoshop

(function () {
    var sourcePath = "/Users/yijin/Downloads/疯狂餐厅(1).psd";
    var outputRoot = "/Users/yijin/gourmetproject/GourmetProject/Assets/GameMain/Content/Resources/Sprites/UI/FengKuangCanTing/Score";
    var specs = [
        { file: "score_background.png", path: "分数/分数/图层 15 拷贝 20", hiddenChildren: [] },
        { file: "score_value_backing.png", path: "分数/分数/图层 17 拷贝 16", hiddenChildren: [] },
        { file: "multiplier_background.png", path: "分数/倍率/图层 15 拷贝 21", hiddenChildren: [] },
        { file: "multiplier_value_backing.png", path: "分数/倍率/图层 17 拷贝 17", hiddenChildren: [] },
        { file: "multiplier_x.png", path: "分数/x", hiddenChildren: [] },
        { file: "dish_value_background.png", path: "分数/美味值/图层 15 拷贝 22", hiddenChildren: [] },
        { file: "dish_value_icon.png", path: "分数/美味值/图层 36", hiddenChildren: [] },
        { file: "dish_value_backing.png", path: "分数/美味值/图层 17 拷贝 18", hiddenChildren: [] }
    ];

    function ensureFolder(path) {
        var folder = new Folder(path);
        if (folder.exists) return folder;
        if (folder.parent && !folder.parent.exists) ensureFolder(folder.parent.fsName);
        if (!folder.create()) throw new Error("Cannot create folder: " + path);
        return folder;
    }

    function findLayer(container, path) {
        var parts = path.split("/");
        var current = container;
        for (var p = 0; p < parts.length; p++) {
            var found = null;
            for (var i = 0; i < current.layers.length; i++) {
                if (current.layers[i].name === parts[p]) {
                    found = current.layers[i];
                    break;
                }
            }
            if (!found) throw new Error("Layer not found: " + path + " (missing " + parts[p] + ")");
            current = found;
        }
        return current;
    }

    function px(value) {
        return Math.round(value.as("px") * 1000) / 1000;
    }

    function layerBounds(layer) {
        var bounds = layer.bounds;
        return [px(bounds[0]), px(bounds[1]), px(bounds[2]), px(bounds[3])];
    }

    function hideDirectChildren(layerSet, names) {
        if (layerSet.typename !== "LayerSet") return;
        for (var i = 0; i < layerSet.layers.length; i++) {
            for (var n = 0; n < names.length; n++) {
                if (layerSet.layers[i].name === names[n]) layerSet.layers[i].visible = false;
            }
        }
    }

    function exportSpec(source, spec) {
        app.activeDocument = source;
        var sourceLayer = findLayer(source, spec.path);
        var bounds = layerBounds(sourceLayer);
        var width = Math.max(1, Math.ceil(bounds[2] - bounds[0]));
        var height = Math.max(1, Math.ceil(bounds[3] - bounds[1]));
        var output = new File(outputRoot + "/" + spec.file);
        if (output.exists) output.remove();

        var target = app.documents.add(width, height, 72, spec.file, NewDocumentMode.RGB, DocumentFill.TRANSPARENT);
        try {
            app.activeDocument = source;
            var copy = sourceLayer.duplicate(target, ElementPlacement.PLACEATBEGINNING);
            app.activeDocument = target;
            copy.visible = true;
            hideDirectChildren(copy, spec.hiddenChildren);

            var copiedBounds = layerBounds(copy);
            copy.translate(UnitValue(-copiedBounds[0], "px"), UnitValue(-copiedBounds[1], "px"));

            var options = new ExportOptionsSaveForWeb();
            options.format = SaveDocumentType.PNG;
            options.PNG8 = false;
            options.transparency = true;
            options.interlaced = false;
            options.includeProfile = false;
            target.exportDocument(output, ExportType.SAVEFORWEB, options);

            return spec.file + "\t" + width + "x" + height + "\t" + spec.path + "\t[" + bounds.join(",") + "]";
        } finally {
            target.close(SaveOptions.DONOTSAVECHANGES);
        }
    }

    var oldDialogs = app.displayDialogs;
    var oldUnits = app.preferences.rulerUnits;
    var source = null;
    try {
        app.displayDialogs = DialogModes.NO;
        app.preferences.rulerUnits = Units.PIXELS;
        ensureFolder(outputRoot);
        source = app.open(new File(sourcePath));
        for (var i = 0; i < specs.length; i++) exportSpec(source, specs[i]);
    } finally {
        if (source) {
            try { source.close(SaveOptions.DONOTSAVECHANGES); } catch (_) {}
        }
        app.preferences.rulerUnits = oldUnits;
        app.displayDialogs = oldDialogs;
    }
})();
