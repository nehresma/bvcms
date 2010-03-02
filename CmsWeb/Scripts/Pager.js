﻿(function($) {
    $.gotoPage = function(e, pg) {
        var f = $(e).closest('form');
        $("#Page", f).val(pg);
        return $.getTable(f);
    }
    $.setPageSize = function(e) {
        var f = $(e).closest('form');
        $('#Page', f).val(1);
        $("#PageSize", f).val($(e).val());
        return $.getTable(f);
    }
    $.getTable = function(f) {
        var q = f.serialize();
        $.post(f.attr('action'), q, function(ret) {
            $(f).html(ret).ready(function() {
                $('table.grid > tbody > tr:even', f).addClass('alt');
                $('table.grid > thead a.sortable', f).click(function() {
                    var newsort = $(this).text();
                    var sort = $("#Sort", f);
                    var dir = $("#Direction", f);
                    if ($(sort).val() == newsort && $(dir).val() == 'asc')
                        $(dir).val('desc');
                    else
                        $(dir).val('asc');
                    $(sort).val(newsort);
                    $.getTable(f);
                    return false;
                });
            });
        });
        return false;
    }
    $.showTable = function(f) {
        if ($('table.grid', f).size() == 0)
            $.getTable(f);
        return false;
    }
})(jQuery);
