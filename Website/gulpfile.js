/* Copyright © 2018 Softel vdm, Inc. - https://yetawf.com/Documentation/YetaWF/Licensing */

// If you get a message like "node sass could not find a binding for your current environment: windows 64-bit with node.js 4.x"
// when running sass, use "npm rebuild node-sass".

var gulp = require('gulp');
var print = require('gulp-print');
var ext_replace = require('gulp-ext-replace');
var replace = require('gulp-replace');
var lec = require('gulp-line-ending-corrector');

var runSequence = require('run-sequence');
gulp.task('DebugBuild', () => {
    runSequence(['sass', 'less'], 'ts');
});
gulp.task('ReleaseBuild', () => {
    runSequence(['sass', 'less', 'less-global'], ['ts', 'tslint'], ["minify-js", "minify-css", "minify-globals-css"]);
});

/* TypeScript Compile */
var ts = require('gulp-typescript');
var sourcemaps = require('gulp-sourcemaps');
var tsFolders = [
            "wwwroot/**/*.ts",
            "wwwroot/**/*.tsx",
            "!wwwroot/**/*.d.ts",
            "!lib/**",
            "!lib",
            "!wwwroot/AddOns/YetaWF/Core/_JS/**",
            "!wwwroot/AddOns/YetaWF/Core/_JS"
];
gulp.task('ts', () => {
    var tsProject = ts.createProject('tsconfig.json');
    gulp.src(tsFolders, { follow: true })
        .pipe(print())
        .pipe(sourcemaps.init())
        .pipe(tsProject())
        .pipe(lec({ eolc: 'CRLF' })) //OMG - We'll deal with it later...
        .pipe(sourcemaps.write('.'))
        .pipe(gulp.dest(function (file) {
            return file.base;
        }));
}
);

/* TypeScript Lint */
var tslint = require("gulp-tslint");
gulp.task("tslint", () =>
    gulp.src(tsFolders, { follow: true })
        .pipe(print())
        .pipe(tslint({
            formatter: "msbuild",
            configuration: "./tslint.json"
        }))
        .pipe(tslint.report({
            reportLimit: 50
        }))
);

/* Scss Compile */
var sass = require('gulp-sass');
var sassFolders = [
            "wwwroot/AddOns/**/*.scss",
            "wwwroot/Vault/**/*.scss",
            "VaultPrivate/**/*.scss",
            "!wwwroot/AddOns/YetaWF/Core/_JS/**",
            "!wwwroot/AddOns/YetaWF/Core/_JS"
];
gulp.task('sass', () =>
    gulp.src(sassFolders, { follow: true })
        .pipe(print())
        .pipe(replace('@import "../../../../../../../../Website/node_modules/bootswatch/','@import "../../../../../../../../../Website/node_modules/bootswatch/'))
        .pipe(sass())
        .pipe(ext_replace('.css'))
        .pipe(lec({ eolc: 'CRLF' })) //OMG - We'll deal with it later...
        .pipe(gulp.dest(function (file) {
            return file.base;
        })
    )
);

/* Less Compile */
var less = require('gulp-less');
var lessFolders = [
            "wwwroot/AddOns/**/*.less",
            "wwwroot/Vault/**/*.less",
            "VaultPrivate/**/*.less",
            //"!AddOns/YetaWF/Core/_JS/**",
            //"!AddOns/YetaWF/Core/_JS",
            "!**/*.min.less",
            "!**/*.pack.less"
]
gulp.task('less', () =>
    gulp.src(lessFolders, { follow: true })
        .pipe(print())
        .pipe(less())
        .pipe(ext_replace(".css"))
        .pipe(lec({ eolc: 'CRLF' })) //OMG - We'll deal with it later...
        .pipe(gulp.dest(function (file) {
            return file.base;
        }))
);


gulp.task('less-global', () =>
    gulp.src([
            "something/**/.less",
            "!**/*.min.less",
            "!**/*.pack.less"
        ], { follow: true })
        .pipe(print())
        .pipe(less())
        .pipe(ext_replace(".css"))
        .pipe(lec({ eolc: 'CRLF' })) //OMG - We'll deal with it later...
        .pipe(gulp.dest(function (file) {
            return file.base;
        }))
);

/* Javascript minify */
var minify = require("gulp-minify");
gulp.task('minify-js', () =>
    gulp.src(["wwwroot/AddOns/**/*.js",
            "wwwroot/AddOnsCustom/**/*.js",
            "node_modules/jquery-validation-unobtrusive/*.js",
            "node_modules/urijs/src/*.js",
            //"!wwwroot/AddOns/YetaWF/Core/_JS/google.com.swfobject/**",
            //"!wwwroot/AddOns/YetaWF/Core/_JS/google.com.swfobject",
            "!**/*.min.js",
            "!**/*.pack.js"
        ], { follow: true })
        .pipe(print())
        .pipe(minify({
            ext: {
                src: '.js',
                min: '.min.js'
            },
            nosource: true,
            ignoreFiles: ['.min.js', '.pack.js']
        }))
        .pipe(gulp.dest(function (file) {
            return file.base;
        }))
);

/* CSS Minify */
var cleanCSS = require('gulp-clean-css');
gulp.task('minify-css', () =>
    gulp.src(["wwwroot/AddOns/**/*.css",
            "wwwroot/AddOnsCustom/**/*.css",
            "wwwroot/Vault/**/*.css",
            "VaultPrivate/**/*.css",
            "node_modules/normalize-css/*.css",
            "node_modules/smartmenus/dist/addons/bootstrap/*.css",
            //"!AddOns/YetaWF/Core/_JS/google.com.swfobject/**",
            //"!AddOns/YetaWF/Core/_JS/google.com.swfobject",
            "!**/*.min.css",
            "!**/*.pack.css"
        ], { follow: true })
        .pipe(print())
        .pipe(cleanCSS({
            compatibility: 'ie8',
            inline: ['local'], // enables local inlining
            rebase: false // don't change url()
        }))
        .pipe(ext_replace(".min.css"))
        .pipe(gulp.dest(function (file) {
            return file.base;
        }))
);

/* CSS Minify for global addons */
gulp.task('minify-globals-css', () =>
    gulp.src(["wwwroot/AddOns/YetaWF/Core/_JS/**/*.css",
            "!**/*.min.css",
            "!**/*.pack.css"
        ], { follow: true })
        .pipe(print())
        .pipe(cleanCSS({
            compatibility: 'ie8',
            inline: ['local'], // enables local inlining
            rebase: false // don't change url()
        }))
        .pipe(ext_replace(".min.css"))
        .pipe(gulp.dest(function (file) {
            return file.base;
        }))
);

gulp.task('watch', function () {
    gulp.watch(tsFolders, ['ts']);
    gulp.watch(sassFolders, ['sass']);
    gulp.watch(lessFolders, ['less']);
});



