using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace LongevityWorldCup.Website.Migrations
{
    [DbContext(typeof(LongevityWorldCup.Website.Business.AgeGuessContext))]
    partial class AgeGuessContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
            modelBuilder.HasAnnotation("ProductVersion", "8.0.3");

            modelBuilder.Entity("LongevityWorldCup.Website.Business.AgeGuess", b =>
            {
                b.Property<int>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("INTEGER");

                b.Property<int>("AthleteId")
                    .HasColumnType("INTEGER");

                b.Property<int>("Guess")
                    .HasColumnType("INTEGER");

                b.Property<string>("FingerprintHash")
                    .IsRequired()
                    .HasColumnType("TEXT");

                b.Property<DateTime>("WhenUtc")
                    .HasColumnType("TEXT");

                b.HasKey("Id");

                b.ToTable("AgeGuesses");
            });
        }
    }
}
