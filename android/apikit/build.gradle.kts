// android/apikit/build.gradle.kts — SLICE_S7_CONTRACT.md §1a/§9: OpenAPI Generator gradle plugin,
// kotlin client + kotlinx-serialization, generated from ../contracts/openapi.v0.json at build time
// (never committed — B17 consumption proof). Transport + the ONE error mapper live here too.
//
// android.yml invokes `:apikit:openApiGenerate :apikit:compileWeebDebugKotlin` directly — both tasks
// must exist, which requires this module to carry the same "brand" flavor dimension as :app.

plugins {
    id("com.android.library")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.serialization")
    id("org.openapi.generator")
}

android {
    namespace = "app.client.apikit"
    compileSdk = 34

    defaultConfig {
        minSdk = 28
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    flavorDimensions += "brand"
    productFlavors {
        create("weeb") { dimension = "brand" }
        create("friki") { dimension = "brand" }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    sourceSets {
        getByName("main") {
            kotlin.srcDir(layout.buildDirectory.dir("generated/openapi/src/main/kotlin"))
        }
    }

    testOptions {
        unitTests {
            isIncludeAndroidResources = true
        }
    }
}

kotlin {
    jvmToolchain(17)
}

// The B17 consumption proof: generate the kotlin+kotlinx-serialization client from the checked-in
// canonical contract at build time. Lint-verify-not-generate applies to BRAND flavor files (§1c); the
// API surface itself is exactly what codegen-from-contract exists for (§1a) — no hand-rolled models.
openApiGenerate {
    generatorName.set("kotlin")
    inputSpec.set(rootDir.resolve("../contracts/openapi.v0.json").absolutePath)
    outputDir.set(layout.buildDirectory.dir("generated/openapi").get().asFile.absolutePath)
    packageName.set("app.client.apikit.generated")
    apiPackage.set("app.client.apikit.generated.api")
    modelPackage.set("app.client.apikit.generated.model")
    library.set("jvm-okhttp4")
    configOptions.set(
        mapOf(
            "serializationLibrary" to "kotlinx_serialization",
            "dateLibrary" to "java8",
        )
    )
    globalProperties.set(
        mapOf(
            "apiTests" to "false",
            "modelTests" to "false",
            "apiDocs" to "false",
            "modelDocs" to "false",
        )
    )
}

tasks.withType<org.jetbrains.kotlin.gradle.tasks.KotlinCompile>().configureEach {
    dependsOn("openApiGenerate")
}

// The generated sourceSet does not exist before the first `openApiGenerate` run; Kotlin's source-set
// wiring above still needs the directory to be creatable at configuration time.
tasks.named("preBuild") {
    dependsOn("openApiGenerate")
}

dependencies {
    implementation("org.jetbrains.kotlinx:kotlinx-serialization-json:1.7.1")
    implementation("com.squareup.okhttp3:okhttp:4.12.0")

    testImplementation("junit:junit:4.13.2")
    testImplementation("org.jetbrains.kotlin:kotlin-test:2.0.20")
    testImplementation("com.squareup.okhttp3:mockwebserver:4.12.0")
}
