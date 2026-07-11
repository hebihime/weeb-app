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
        // java.srcDir (not kotlin.srcDir): `java` is a guaranteed member of AGP's AndroidSourceSet, so
        // the build SCRIPT always compiles; the `.kotlin` DSL accessor is added by KGP and is not
        // guaranteed to resolve across AGP/KGP combos (a miss there fails configuration entirely). The
        // kotlin-android plugin compiles .kt files found in the java source dirs, so the generated
        // Kotlin client is picked up either way. The `dependsOn("openApiGenerate")` below guarantees
        // the dir is populated before compilation.
        getByName("main") {
            java.srcDir(layout.buildDirectory.dir("generated/openapi/src/main/kotlin").get().asFile)
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
    // The contract is frozen + already validated upstream by tools/contract-lint; the spec uses
    // OpenAPI 3.1 union types (e.g. Problem.status is ["integer","string"]) that swagger-parser's
    // validator can be over-strict about. Skip re-validating here so a valid-but-unusual 3.1 construct
    // can never hard-fail the codegen job — generation itself still degrades gracefully (a union type
    // becomes kotlin.Any / a picked member), which compiles either way.
    validateSpec.set(false)
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
